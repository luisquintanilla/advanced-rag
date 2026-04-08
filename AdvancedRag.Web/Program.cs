using Microsoft.Extensions.AI;
using AdvancedRag.Web.Components;
using AdvancedRag.Web.Services;
using AdvancedRag.Web.Services.Ingestion;
using MEDIExtensions.DependencyInjection;
using MEDIExtensions.Retrieval;
using UglyToad.PdfPig.DataIngestion.Processors;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var openai = builder.AddAzureOpenAIClient("openai");
openai.AddChatClient("chat")
    .UseFunctionInvocation()
    .UseOpenTelemetry()
    .UseLogging();
openai.AddEmbeddingGenerator("embedding");

builder.AddQdrantClient("vectordb");
builder.Services.AddQdrantVectorStore();
builder.Services.AddQdrantCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName);

// Ingestion pipeline — compose document and chunk processors
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddIngestionPipeline()
    .UseDocumentProcessor<VisionOcrEnricher>()
    .UseDocumentProcessor<VisionTableEnricher>()
    .UseChunkProcessor<ContextualChunkEnricher>()
    .UseEntityExtraction()
    .UseTopicClassification(o => o.Taxonomy = ["web", "data", "performance", "security", "architecture", "testing", "cloud", "ai"])
    .UseHypotheticalQueries()
    .UseTreeIndex();

// Retrieval pipeline — compose query and result processors
builder.Services.AddRetrievalPipeline()
    .UseQueryExpansion()
    .UseTreeSearch()
    .UseLlmReranking()
    .UseCrag();

// SemanticSearch depends on both DataIngestor and RetrievalPipeline
builder.Services.AddSingleton<SemanticSearch>();

// Generation orchestrators (standalone, registered directly)
builder.Services.AddSingleton(sp =>
    new SelfRagOrchestrator(
        sp.GetRequiredService<IChatClient>(),
        sp.GetService<ILoggerFactory>()));

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
