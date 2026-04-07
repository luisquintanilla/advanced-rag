using Microsoft.Extensions.AI;
using MEDIExtensions.Retrieval;
using PdfAIngest.Web.Components;
using PdfAIngest.Web.Services;
using PdfAIngest.Web.Services.Ingestion;

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
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));

// Register RetrievalPipeline with configurable processors from appsettings.json
builder.Services.AddSingleton(sp =>
{
    var config = builder.Configuration.GetSection("Retrieval");
    var loggerFactory = sp.GetService<ILoggerFactory>();
    var chatClient = sp.GetRequiredService<IChatClient>();
    var pipeline = new RetrievalPipeline(loggerFactory: loggerFactory);

    // Pre-query processor (pick one)
    var queryStrategy = config["QueryStrategy"] ?? "None";
    if (queryStrategy == "QueryExpansion")
        pipeline.QueryProcessors.Add(new MultiQueryExpander(chatClient));
    else if (queryStrategy == "HyDE")
        pipeline.QueryProcessors.Add(new HydeQueryTransformer(chatClient));

    // Post-search: Reranker
    var reranker = config["Reranker"] ?? "None";
    if (reranker == "Llm")
        pipeline.ResultProcessors.Add(new LlmReranker(chatClient));

    // Post-search: CRAG quality gate
    if (config.GetValue<bool>("EnableCrag"))
        pipeline.ResultProcessors.Add(new CragValidator(chatClient));

    return pipeline;
});

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
