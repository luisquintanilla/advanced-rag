using Microsoft.Extensions.AI;
using AdvancedRag.Web.Components;
using AdvancedRag.Web.Extensions;
using AdvancedRag.Web.Services;
using AdvancedRag.Web.Services.Ingestion;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var openai = builder.AddAzureOpenAIClient("openai");
openai.AddChatClient("chat")
    .UseFunctionInvocation()
    .UseOpenTelemetry()
    .UseLogging();
openai.AddEmbeddingGenerator("embedding");

// Speculative RAG needs a second "drafter" chat client
var generationMode = builder.Configuration[$"{RetrievalOptions.SectionName}:GenerationMode"];
if (generationMode == nameof(GenerationMode.SpeculativeRag))
{
    var drafterModel = builder.Configuration[$"{RetrievalOptions.SectionName}:DrafterModel"] ?? "chat";
    openai.AddKeyedChatClient("drafter", drafterModel);
}

builder.AddQdrantClient("vectordb");
builder.Services.AddQdrantVectorStore();
builder.Services.AddQdrantCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName);
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));

builder.Services.AddRetrievalPipeline(builder.Configuration);
builder.Services.AddIngestionServices();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
