# Advanced RAG Reference Application

A .NET reference application demonstrating advanced RAG (Retrieval-Augmented Generation) patterns with [MEDIExtensions](../MEDIExtensions/). Forked from the [AI Chat Template](https://github.com/dotnet/extensions) and extended with configuration-driven retrieval pipeline, ingestion enrichers, and generation orchestrators.

## What This Demonstrates

Every advanced RAG pattern is selectable via `appsettings.json` — no code changes:

| Feature | Options | Default |
|---------|---------|---------|
| Query strategy | `None`, `QueryExpansion`, `HyDE` | None |
| Reranker | `None`, `Llm`, `CrossEncoder` | None |
| CRAG quality gate | `true`, `false` | false |
| Generation mode | `Standard`, `SelfRag`, `SpeculativeRag` | Standard |
| Search paradigm | `Vector`, `TreeTraversal`, `Adaptive` | Vector |
| Entity extraction | `true`, `false` | true |
| Topic classification | `true`, `false` | true |
| Hypothetical queries | `true`, `false` | false |
| Tree index | `true`, `false` | false |

## Configuration

```json
{
  "Retrieval": {
    "QueryStrategy": "None",
    "Reranker": "None",
    "EnableCrag": false,
    "GenerationMode": "Standard",
    "SearchParadigm": "Vector",
    "DrafterModel": "chat"
  },
  "Ingestion": {
    "EnableEntityExtraction": true,
    "EnableTopicClassification": true,
    "EnableHypotheticalQueries": false,
    "EnableTreeIndex": false,
    "TopicTaxonomy": ["web", "data", "performance", "security", "architecture", "testing", "cloud", "ai"]
  }
}
```

## Architecture

```
AdvancedRag.AppHost          ← Aspire orchestrator (Azure OpenAI + Qdrant)
AdvancedRag.ServiceDefaults  ← OpenTelemetry, health checks, service discovery
AdvancedRag.Web              ← Blazor Server chat UI + RAG pipeline
  ├─ Program.cs              ← DI registration (configurable processors from settings)
  ├─ Services/
  │   ├─ SemanticSearch.cs   ← Uses RetrievalPipeline instead of raw vector search
  │   ├─ IngestedChunk.cs    ← Extended with entity, topic, tree metadata fields
  │   └─ Ingestion/
  │       ├─ DataIngestor.cs ← MEDI pipeline + configurable enrichers
  │       └─ DocumentReader.cs ← PDF (PdfPig vision) + Markdown
  └─ Components/             ← Blazor chat UI
```

## Key Patterns

### Configurable Retrieval Pipeline (Program.cs)

The retrieval pipeline is assembled from `appsettings.json` at startup:

```csharp
var pipeline = new RetrievalPipeline(loggerFactory: loggerFactory);

// Pre-search processor (pick one)
if (queryStrategy == "QueryExpansion")
    pipeline.QueryProcessors.Add(new MultiQueryExpander(chatClient));
else if (queryStrategy == "HyDE")
    pipeline.QueryProcessors.Add(new HydeQueryTransformer(chatClient));

// Post-search: reranker + quality gate
if (reranker == "Llm")
    pipeline.ResultProcessors.Add(new LlmReranker(chatClient));
if (enableCrag)
    pipeline.ResultProcessors.Add(new CragValidator(chatClient));
```

### Speculative RAG with Keyed DI

Uses .NET's keyed DI for multi-model management — a drafter (small/fast) and verifier (large/accurate):

```csharp
openai.AddKeyedChatClient("drafter", config["DrafterModel"]);
builder.Services.AddSingleton(sp =>
{
    var drafter = sp.GetRequiredKeyedService<IChatClient>("drafter");
    var verifier = sp.GetRequiredService<IChatClient>();
    return new SpeculativeRagOrchestrator(drafter, verifier);
});
```

### Ingestion Enrichers (DataIngestor.cs)

MEDI `ChunkProcessors` are added conditionally from config:

```csharp
if (ingestionConfig.GetValue<bool>("EnableEntityExtraction"))
    pipeline.ChunkProcessors.Add(new EntityExtractionProcessor(chatClient));

if (ingestionConfig.GetValue<bool>("EnableTopicClassification"))
    pipeline.ChunkProcessors.Add(new TopicClassificationProcessor(chatClient, taxonomy));
```

## Evaluation

`ValidateRetrieval.cs` is a standalone MEAI Evaluation A/B comparison tool:

```bash
dotnet run ValidateRetrieval.cs
```

Compares baseline (raw vector search) vs enhanced (with query expansion, reranking, CRAG) using `RelevanceTruthAndCompletenessEvaluator`.

## Prerequisites

- [.NET 10 SDK](https://dot.net)
- [Docker Desktop](https://www.docker.com/) (for Qdrant and optionally Ollama)
- Azure OpenAI resource (or Ollama for local development)

## Running

1. Configure Azure OpenAI credentials in AppHost user secrets:
   ```bash
   cd AdvancedRag.AppHost
   dotnet user-secrets set "AzureOpenAI:Name" "<resource-name>"
   dotnet user-secrets set "AzureOpenAI:ResourceGroup" "<resource-group>"
   ```

2. Run via Aspire:
   ```bash
   dotnet run --project AdvancedRag.AppHost
   ```

3. Experiment with patterns by editing `AdvancedRag.Web/appsettings.json`.

## Dependencies

- [MEDIExtensions](../MEDIExtensions/) — Retrieval + ingestion processors (project reference)
- [MEDI](https://www.nuget.org/packages/Microsoft.Extensions.DataIngestion) — Ingestion pipeline
- [PdfPig.DataIngestion](https://www.nuget.org/packages/UglyToad.PdfPig.DataIngestion) — PDF → vision OCR
- [Aspire](https://learn.microsoft.com/dotnet/aspire/) — Orchestration, service discovery, OTel
- [Qdrant](https://qdrant.tech/) — Vector store

## Related

- [MEDIExtensions](../MEDIExtensions/) — The library this app consumes
- [medi-advanced-rag-investigation](../medi-advanced-rag-investigation/) — Research that produced these patterns
