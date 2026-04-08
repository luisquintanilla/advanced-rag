# Advanced RAG Reference Application

A .NET reference application demonstrating advanced RAG (Retrieval-Augmented Generation) patterns built on composable pipeline APIs from [MEDIExtensions](../MEDIExtensions/). Forked from the [AI Chat Template](https://github.com/dotnet/extensions) and extended with fluent retrieval and ingestion pipelines, RAPTOR tree indexing, LLM reranking, CRAG quality gates, and generation orchestrators.

See [docs/architecture-comparison.md](docs/architecture-comparison.md) for a deep dive on what this project innovates over the base template.

## What This Demonstrates

Composable RAG pipelines configured via fluent builder APIs — the same pattern as `AddChatClient().UseFunctionInvocation()`:

```csharp
// Ingestion: vision OCR + enrichment + RAPTOR tree indexing
builder.Services.AddIngestionPipeline()
    .UseDocumentProcessor<VisionOcrEnricher>()
    .UseDocumentProcessor<VisionTableEnricher>()
    .UseChunkProcessor<ContextualChunkEnricher>()
    .UseEntityExtraction()
    .UseTopicClassification(o => o.Taxonomy = ["web", "data", "security", ...])
    .UseHypotheticalQueries()
    .UseTreeIndex();

// Retrieval: query expansion + tree search + reranking + quality gate
builder.Services.AddRetrievalPipeline()
    .UseQueryExpansion()
    .UseTreeSearch()
    .UseLlmReranking()
    .UseCrag();
```

Every `.UseX()` is optional, has per-processor options, and chains in execution order.

| Stage | Processors | Purpose |
|-------|-----------|---------|
| **Ingestion — Documents** | VisionOcrEnricher, VisionTableEnricher | Vision-AI PDF processing (PdfPig) |
| **Ingestion — Chunks** | ContextualChunkEnricher, EntityExtraction, TopicClassification, HypotheticalQueries, TreeIndex | Semantic enrichment + RAPTOR hierarchy |
| **Retrieval — Query** | QueryExpansion, TreeSearch, HyDE, AdaptiveRouting | Multi-query + level-aware search |
| **Retrieval — Results** | LlmReranking, CRAG | Quality scoring + corrective gate |
| **Generation** | SelfRagOrchestrator, SpeculativeRagOrchestrator | Quality feedback loops |

## Architecture

```
AdvancedRag.AppHost          ← Aspire orchestrator (Azure OpenAI + Qdrant)
AdvancedRag.ServiceDefaults  ← OpenTelemetry, health checks, service discovery
AdvancedRag.Web              ← Blazor Server chat UI + RAG pipelines
  ├─ Program.cs              ← Fluent pipeline composition
  ├─ Services/
  │   ├─ SemanticSearch.cs   ← Uses RetrievalPipeline (not raw vector search)
  │   ├─ IngestedChunk.cs    ← Extended with entity, topic, tree metadata
  │   └─ Ingestion/
  │       ├─ DataIngestor.cs ← Uses IngestionPipelineBuilder<string>.Build()
  │       └─ DocumentReader.cs ← PDF (PdfPig vision) + Markdown
  └─ Components/             ← Blazor chat UI
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/) (for Qdrant vector store)
- Azure OpenAI resource with a vision-capable chat model and an embedding model

## Running

1. Configure Azure OpenAI credentials in AppHost user secrets:
   ```bash
   cd AdvancedRag.AppHost
   dotnet user-secrets set "Azure:SubscriptionId" "<your-subscription-id>"
   dotnet user-secrets set "Azure:Location" "<your-location>"
   dotnet user-secrets set "AzureOpenAI:Name" "<resource-name>"
   dotnet user-secrets set "AzureOpenAI:ResourceGroup" "<resource-group>"
   ```

2. Run via Aspire:
   ```bash
   dotnet run --project AdvancedRag.AppHost
   ```

## Validation

```bash
# Test ingestion pipeline stages
dotnet run ValidateIngestion.cs

# Compare baseline vs. enhanced retrieval
dotnet run ValidateRetrieval.cs
```

## Dependencies

- [MEDIExtensions](../MEDIExtensions/) — Fluent builders + retrieval/ingestion processors (NuGet: `1.0.0-dev`)
- [dotnet/extensions fork](../extensions/) — Retrieval pipeline abstractions (NuGet: `10.5.1-dev`)
- [UglyToad.PdfPig.DataIngestion](https://www.nuget.org/packages/UglyToad.PdfPig.DataIngestion) — Vision-AI PDF processing
- [Aspire](https://learn.microsoft.com/dotnet/aspire/) — Orchestration, service discovery, OpenTelemetry
- [Qdrant](https://qdrant.tech/) — Vector store

## Related

- [docs/architecture-comparison.md](docs/architecture-comparison.md) — Deep dive: what this innovates over the template
- [MEDIExtensions](../MEDIExtensions/) — The library providing pipeline builders and processors
- [medi-advanced-rag-investigation](../medi-advanced-rag-investigation/) — Research that produced these patterns
