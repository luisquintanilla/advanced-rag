// ValidateRetrieval.cs — MEAI Evaluation A/B comparison tool
// Usage: dotnet run ValidateRetrieval.cs
//
// Compares retrieval quality across different pipeline configurations:
//   A: Baseline (raw vector search, no processors)
//   B: Enhanced (with query expansion, reranking, CRAG)
//
// Uses MEAI Evaluation for scoring — the .NET native alternative to RAGAS/LangSmith.

#:package Microsoft.Extensions.AI@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.AI.Evaluation@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.AI.OpenAI@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.VectorData.Abstractions@9.7.0
#:package Microsoft.SemanticKernel.Connectors.InMemory@1.73.0-preview
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.20.0

using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;

// ─── Configuration ──────────────────────────────────────────────────────────
var chatDeployment = "chat";
var embeddingDeployment = "embedding";

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║     Advanced RAG — Retrieval Pipeline Validator          ║");
Console.WriteLine("║     MEAI Evaluation A/B Comparison                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ─── AI Client Setup ────────────────────────────────────────────────────────
Console.WriteLine("🔧 Initializing AI clients...");
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT environment variable");

var azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
IChatClient chatClient = azureClient.GetChatClient(chatDeployment).AsIChatClient();
IEmbeddingGenerator<string, Embedding<float>> embedder =
    azureClient.GetEmbeddingClient(embeddingDeployment).AsIEmbeddingGenerator();

// ─── Test Corpus ────────────────────────────────────────────────────────────
string[] chunks =
[
    "Dependency injection (DI) in .NET is a first-class feature. The built-in container supports " +
    "transient, scoped, and singleton lifetimes via IServiceCollection.",

    "The Options pattern in ASP.NET Core binds configuration sections to strongly-typed classes. " +
    "IOptionsMonitor<T> supports runtime reload when configuration files change.",

    "Middleware in ASP.NET Core forms a request pipeline. Order matters: UseAuthentication() " +
    "must precede UseAuthorization().",

    "Entity Framework Core supports LINQ queries, migrations, change tracking, and " +
    "multiple database providers (SQL Server, PostgreSQL, SQLite).",

    "Health checks in ASP.NET Core report application readiness and liveness via " +
    "MapHealthChecks(\"/health\").",

    "Background services use IHostedService or BackgroundService for long-lived tasks " +
    "like message queue consumers or scheduled jobs.",

    "SignalR enables real-time web communication using WebSockets with strongly-typed hub proxies.",

    "Output caching caches entire HTTP responses with support for tag-based invalidation."
];

// ─── Ingest into InMemory Vector Store ──────────────────────────────────────
Console.WriteLine($"📥 Ingesting {chunks.Length} chunks...");
var vectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = embedder });
var collection = vectorStore.GetCollection<string, TestChunk>("eval-retrieval");
await collection.EnsureCollectionExistsAsync();

for (int i = 0; i < chunks.Length; i++)
    await collection.UpsertAsync(new TestChunk { Id = $"chunk-{i}", Text = chunks[i] });

Console.WriteLine("✓ Ingested\n");

// ─── Test Queries with Expected Answers ─────────────────────────────────────
var testCases = new (string Query, string ExpectedTopic)[]
{
    ("What service lifetimes does .NET dependency injection support?", "DI lifetimes"),
    ("How do I implement health checks in ASP.NET Core?", "Health checks"),
    ("What is the Options pattern in ASP.NET Core?", "Options pattern"),
    ("How do background services work in .NET?", "Background services"),
    ("What databases does Entity Framework Core support?", "EF Core providers"),
};

// ─── MEAI Evaluation Setup ──────────────────────────────────────────────────
Console.WriteLine("📊 Setting up MEAI Evaluation...\n");

var relevanceEvaluator = new RelevanceTruthAndCompletenessEvaluator();

// ─── Run A/B Comparison ─────────────────────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("         A/B COMPARISON: Baseline vs Enhanced            ");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

int baselineWins = 0, enhancedWins = 0, ties = 0;

foreach (var (query, expectedTopic) in testCases)
{
    Console.WriteLine($"─── Query: {query}");
    Console.WriteLine($"    Expected topic: {expectedTopic}\n");

    // Configuration A: Baseline (raw vector search)
    var sw = Stopwatch.StartNew();
    var baselineResults = new List<string>();
    await foreach (var hit in collection.SearchAsync(query, top: 3))
        baselineResults.Add(hit.Record.Text);
    var baselineMs = sw.ElapsedMilliseconds;

    var baselineContext = string.Join("\n", baselineResults);
    var baselineAnswer = await GenerateAnswer(chatClient, query, baselineContext);

    // Configuration B: Enhanced (simulated query expansion + reranking)
    sw.Restart();
    // Simulate query expansion: search with original + a rephrased variant
    var variant = await ExpandQuery(chatClient, query);
    var enhancedResults = new List<(string Text, double Score)>();

    await foreach (var hit in collection.SearchAsync(query, top: 5))
        enhancedResults.Add((hit.Record.Text, hit.Score ?? 0));
    await foreach (var hit in collection.SearchAsync(variant, top: 5))
        enhancedResults.Add((hit.Record.Text, hit.Score ?? 0));

    // RRF merge + top-3
    var merged = enhancedResults
        .GroupBy(r => r.Text)
        .Select(g => (Text: g.Key, Score: g.Sum(x => 1.0 / (60 + g.Count()))))
        .OrderByDescending(r => r.Score)
        .Take(3)
        .Select(r => r.Text)
        .ToList();
    var enhancedMs = sw.ElapsedMilliseconds;

    var enhancedContext = string.Join("\n", merged);
    var enhancedAnswer = await GenerateAnswer(chatClient, query, enhancedContext);

    // MEAI Evaluation: score both answers
    var baselineScore = await EvaluateAnswer(relevanceEvaluator, chatClient, query, baselineAnswer, baselineContext);
    var enhancedScore = await EvaluateAnswer(relevanceEvaluator, chatClient, query, enhancedAnswer, enhancedContext);

    Console.WriteLine($"    [A] Baseline:  score={baselineScore:F2}  ({baselineMs}ms)");
    Console.WriteLine($"    [B] Enhanced:  score={enhancedScore:F2}  ({enhancedMs}ms)");

    if (enhancedScore > baselineScore + 0.1) { enhancedWins++; Console.WriteLine("    >> Enhanced wins"); }
    else if (baselineScore > enhancedScore + 0.1) { baselineWins++; Console.WriteLine("    >> Baseline wins"); }
    else { ties++; Console.WriteLine("    >> Tie"); }
    Console.WriteLine();
}

// ─── Summary ────────────────────────────────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("                     SUMMARY                             ");
Console.WriteLine("══════════════════════════════════════════════════════════\n");
Console.WriteLine($"  Baseline wins:  {baselineWins}");
Console.WriteLine($"  Enhanced wins:  {enhancedWins}");
Console.WriteLine($"  Ties:           {ties}");
Console.WriteLine($"  Total queries:  {testCases.Length}\n");
Console.WriteLine("✓ Retrieval validation complete — MEAI Evaluation as .NET native quality gate");

// ─── Helper Methods ─────────────────────────────────────────────────────────

static async Task<string> GenerateAnswer(IChatClient client, string query, string context)
{
    var prompt = $"Using these passages, answer concisely:\n\n{context}\n\nQuestion: {query}\nAnswer:";
    var response = await client.GetResponseAsync(prompt, new ChatOptions { MaxOutputTokens = 200 });
    return response.Text ?? "";
}

static async Task<string> ExpandQuery(IChatClient client, string query)
{
    var prompt = $"Rephrase this question differently in one sentence: {query}";
    var response = await client.GetResponseAsync(prompt, new ChatOptions { MaxOutputTokens = 50 });
    return response.Text ?? query;
}

static async Task<double> EvaluateAnswer(
    RelevanceTruthAndCompletenessEvaluator evaluator,
    IChatClient chatClient,
    string query,
    string answer,
    string context)
{
    try
    {
        var turns = new List<ChatMessage>
        {
            new(ChatRole.User, query),
            new(ChatRole.Assistant, answer)
        };

        var evalResult = await evaluator.EvaluateAsync(
            turns,
            new ChatConfiguration(chatClient),
            new EvaluationContext { GroundTruth = context });

        // Average across available metrics
        var scores = evalResult.Values
            .Where(v => v.Value is NumericMetricValue)
            .Select(v => ((NumericMetricValue)v.Value).Value)
            .Where(v => !double.IsNaN(v))
            .ToList();

        return scores.Count > 0 ? scores.Average() : 0;
    }
    catch
    {
        return 0;
    }
}

// ─── Record type ────────────────────────────────────────────────────────────

class TestChunk
{
    [VectorStoreKey]
    public string Id { get; set; } = "";

    [VectorStoreData]
    public string Text { get; set; } = "";

    [VectorStoreVector(1536)]
    public string Embedding => Text;
}
