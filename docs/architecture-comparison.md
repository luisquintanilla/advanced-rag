# Beyond Search: Composable RAG Pipelines for .NET Applications

## The Problem: Why "Embed and Search" Isn't Enough

The standard [`AIChatWeb-CSharp` template](https://github.com/dotnet/extensions/tree/main/src/ProjectTemplates/Microsoft.Extensions.AI.Templates/templates/AIChatWeb-CSharp) from `dotnet/extensions` gives you a working RAG chat application. Documents go in, they get chunked and embedded, and when a user asks a question, the system finds the nearest vectors and passes them to the LLM.

This works — for easy questions against clean documents. But the moment your corpus grows, your questions get nuanced, or your documents contain mixed-quality content, the cracks appear:

- **One query, one interpretation.** The user asks "How do I handle auth?" — but the system only searches that exact phrasing. It misses chunks about "authentication middleware," "JWT configuration," or "identity providers."
- **No quality signal.** The top-5 nearest vectors might include chunks that are *semantically similar but factually irrelevant.* The system has no way to know.
- **Flat chunks, flat search.** A 200-page technical manual is sliced into token-counted chunks with no awareness of hierarchy, entities, or topics. A question about the "architecture overview" returns the same type of chunks as one about a specific API parameter.
- **Generate and pray.** The LLM gets context and generates an answer. If the context was bad, the answer is bad. There's no self-correction loop.

These aren't edge cases — they're the *normal* experience for enterprise RAG applications. The template gives you the foundation. This project shows what you build on top of it.

## The Innovation: Pipelines, Not Point Solutions

Rather than fixing one problem at a time, this project introduces **composable pipelines** at every stage of the RAG loop — ingestion, retrieval, and generation — following the same pattern as [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai):

```csharp
// The template gives you this:
var results = await vectorCollection.SearchAsync(query, top: 5);

// This project gives you this:
builder.Services.AddRetrievalPipeline()
    .UseQueryExpansion()          // 1 query → N variants, merged with RRF
    .UseTreeSearch()              // navigate RAPTOR hierarchy
    .UseLlmReranking()            // LLM scores each result for relevance
    .UseCrag();                   // quality gate: accept, refine, or reject

builder.Services.AddIngestionPipeline()
    .UseDocumentProcessor<VisionOcrEnricher>()
    .UseDocumentProcessor<VisionTableEnricher>()
    .UseChunkProcessor<ContextualChunkEnricher>()
    .UseEntityExtraction()        // tag: people, orgs, technologies
    .UseTopicClassification()     // classify into taxonomy
    .UseHypotheticalQueries()     // reverse-HyDE: "what question does this answer?"
    .UseTreeIndex();              // RAPTOR: leaf → branch → root summaries
```

Every `.UseX()` is optional. Every one has per-processor options (`o => o.MaxResults = 10`). The fluent chain reads top-to-bottom as the execution flow. This follows the same pattern as `AddChatClient().UseFunctionInvocation().UseOpenTelemetry()` — because if you know how to compose MEAI middleware, you already know how to compose RAG pipelines.

## Architecture: Three Layers of Enhancement

### Layer 1: Smarter Ingestion

The template chunks documents by token count and stores embeddings. This project adds four enrichment processors that run *during* ingestion, before anything hits the vector store:

```
┌───────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Chunked  │────▶│   Entity     │────▶│   Topic      │────▶│ Hypothetical │
│  Content  │     │  Extraction  │     │Classification│     │   Queries    │
└───────────┘     └──────────────┘     └──────────────┘     └──────────────┘
                  people, orgs,          "security",          "What are the
                  versions               "architecture"       auth options?"
                        │                      │                     │
                        ▼                      ▼                     ▼
                  ┌──────────────────────────────────────────────────────┐
                  │                   Tree Indexer                       │
                  │  leaf chunks → branch summaries → root summary      │
                  └──────────────────────────────────────────────────────┘
                                         │
                                         ▼
                  ┌──────────────────────────────────────────────────────┐
                  │              Vector Store (Qdrant)                   │
                  │  enriched chunks + summaries at multiple levels      │
                  └──────────────────────────────────────────────────────┘
```

| Processor | What It Does | Why It Matters |
|-----------|-------------|----------------|
| **Entity Extraction** | Tags chunks with people, organizations, technologies, and version numbers via LLM | Enables filtered search ("show me chunks about React") without keyword matching |
| **Topic Classification** | Classifies each chunk into a configurable taxonomy (e.g., security, architecture, performance) | Faceted retrieval; chunks carry semantic categories, not just embeddings |
| **Hypothetical Queries** | Generates 3 questions each chunk answers (reverse-HyDE) | Bridges the vocabulary gap: user queries match hypothetical queries better than raw content |
| **Tree Index** | Creates RAPTOR-style hierarchical summaries: document-level (branch) and corpus-level (root) | A search for "system architecture" can match a root-level overview instead of only leaf fragments |

These compose in order — entity extraction runs first, then topic classification, then hypothetical queries, and finally tree indexing. Tree indexing is last because it summarizes *enriched* chunks, capturing entities and topics in the summaries.

### Layer 2: Smarter Retrieval

The template does `collection.SearchAsync(query, top: 5)`. This project wraps that in a `RetrievalPipeline` with pre-search query processing and post-search result processing:

```
┌──────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  User     │────▶│   Query      │────▶│    Tree      │────▶│   Vector     │
│  Query    │     │  Expansion   │     │   Search     │     │   Search     │
└──────────┘     └──────────────┘     └──────────────┘     └──────────────┘
                  1 → 4 variants       level-aware           per-variant
                  (original + 3)       grouping              RRF merge
                        │                                         │
                        └────────────────────┬────────────────────┘
                                             ▼
                  ┌──────────────┐     ┌──────────────┐
                  │    LLM       │────▶│    CRAG      │────▶  Final Results
                  │  Reranking   │     │ Quality Gate │
                  └──────────────┘     └──────────────┘
                  pointwise scoring    accept / refine /
                  of each result       reject
```

**Query Processors** (pre-search — modify or expand the query):

| Processor | What It Does | When to Use |
|-----------|-------------|-------------|
| **Query Expansion** | LLM generates 3 alternate phrasings. Each variant is searched independently; results are merged using Reciprocal Rank Fusion. | Default — significantly improves recall with minimal latency |
| **HyDE** | LLM generates a *hypothetical perfect answer*, then searches for chunks similar to that answer instead of the original query. | Factual/technical queries where the answer structure is predictable |
| **Tree Search** | Signals the pipeline to group results by tree level (root/branch/leaf) set by TreeIndexProcessor during ingestion. Returns leaf chunks prioritized, with branch and root summaries for context. | When documents are large and hierarchical; pairs with UseTreeIndex() |
| **Adaptive Routing** | LLM classifies the query type and routes to the best search paradigm. | When your corpus has diverse query patterns (exploratory vs. specific) |

**Result Processors** (post-search — filter, reorder, or validate results):

| Processor | What It Does | When to Use |
|-----------|-------------|-------------|
| **LLM Reranking** | LLM scores each result for query relevance on a 1-10 scale. Results are reordered by LLM judgment, not just embedding distance. | When embedding similarity produces false positives (common for technical content) |
| **CRAG** | Corrective Retrieval Augmented Generation — evaluates the top-N results and classifies confidence as Correct (use as-is), Ambiguous (refine), or Incorrect (reject and suggest web search). | When answer quality matters more than latency; prevents "confident but wrong" answers |

### Layer 3: Smarter Generation

The template passes retrieved context to the LLM and returns the response. This project adds generation orchestrators that add quality feedback loops:

| Orchestrator | How It Works | Trade-off |
|-------------|-------------|-----------|
| **Self-RAG** | Generate → evaluate quality → if below threshold, retry with different context selection. Up to N retries. | 2-3x LLM calls, but catches low-quality generations before the user sees them |
| **Speculative RAG** | A fast/cheap "drafter" model generates N candidate answers in parallel. A larger "verifier" model selects the best. Uses keyed DI for multi-model management. | Draft calls are cheap and parallel; verification adds one more call. Total latency is often *lower* than a single large-model call. |

## The Composable Builder Pattern

The APIs follow three design principles from `Microsoft.Extensions.AI`:

**1. Fluent chaining** — each `.UseX()` returns the builder, enabling composition:
```csharp
builder.Services.AddRetrievalPipeline()
    .UseQueryExpansion(o => o.VariantCount = 5)
    .UseLlmReranking(o => o.MaxResults = 10)
    .UseCrag(o => o.EvaluateTopN = 5);
```

**2. Named convenience + generic extensibility** — built-in processors have discoverable methods with typed options; third-party processors use generics:
```csharp
// Built-in (discoverable, typed options)
.UseLlmReranking(o => o.MaxCandidates = 8)

// Third-party (extensible, ActivatorUtilities-resolved)
.UseResultProcessor<MyCustomReranker>()
```

**3. Registration order = execution order** — processors chain sequentially. The fluent chain reads top-to-bottom as the pipeline executes:
```csharp
.UseEntityExtraction()        // runs first: tags chunks
.UseTopicClassification()     // runs second: classifies tagged chunks
.UseTreeIndex()               // runs last: summarizes enriched chunks
```

## RAPTOR: End-to-End Hierarchical RAG

One of the most powerful patterns demonstrated here is the full [RAPTOR](https://arxiv.org/abs/2401.18059) loop — hierarchical indexing during ingestion paired with level-aware retrieval:

**During ingestion** (`UseTreeIndex()`):
- Leaf chunks (level 0) are the original content
- Branch summaries (level 1) are LLM-generated document-level overviews
- Root summary (level 2) is an LLM-generated corpus-level overview
- All levels are stored in the same vector collection with `Level` metadata

**During retrieval** (`UseTreeSearch()`):
- The pipeline searches with a wider net (`topK × 3`)
- Results are grouped by `Level` metadata
- Leaf chunks are prioritized as primary results
- Branch and root summaries provide contextual scaffolding
- The LLM gets both specific detail AND high-level context

This means a query like *"What is the overall architecture?"* can match a root-level summary directly, while *"How do I configure JWT?"* matches specific leaf chunks — both from the same search call.

## From Template to Advanced RAG: What Changed

| Aspect | Template | Advanced RAG |
|--------|----------|-------------|
| **Ingestion** | Token chunker → embed → store | Semantic chunker + 4 enrichment processors + tree indexing → embed → store |
| **Retrieval** | `collection.SearchAsync(query, top: 5)` | Query expansion + tree search → vector search with RRF → LLM reranking + CRAG |
| **Generation** | Context → LLM → response | Self-RAG (quality loop) or Speculative RAG (draft → verify) |
| **DI registration** | Inline `new` / manual wiring | `AddRetrievalPipeline().UseX()` / `AddIngestionPipeline().UseX()` |
| **Pipeline composition** | Not applicable — no pipeline concept | Fluent builders with per-processor `Action<TOptions>` callbacks |
| **Configuration** | N/A | Compile-time composition via fluent API (not runtime config) |
| **PDF processing** | MarkItDown MCP or basic PdfPig | Vision-AI via PdfPig (VisionOcrEnricher + VisionTableEnricher) |

## The Three-Repo Ecosystem

This project is part of a three-repo architecture:

```
┌─────────────────────────────────────────────────────────────┐
│  dotnet/extensions fork (feature/retrieval-abstractions)    │
│  Adds: RetrievalPipeline, RetrievalQuery, RetrievalChunk,  │
│        RetrievalQueryProcessor, RetrievalResultProcessor    │
│  Packages: Microsoft.Extensions.DataIngestion 10.5.1-dev    │
└──────────────────────────┬──────────────────────────────────┘
                           │ NuGet (local-packages/)
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  MEDIExtensions (library)                                   │
│  Concrete processors: MultiQueryExpander, LlmReranker,      │
│    CragValidator, TreeSearchRetriever, EntityExtraction...   │
│  Fluent builders: AddRetrievalPipeline(), AddIngestion...    │
│  Package: MEDIExtensions 1.0.0-dev                          │
└──────────────────────────┬──────────────────────────────────┘
                           │ NuGet (local-packages/)
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  AdvancedRag (this repo — reference application)            │
│  Blazor Server + Aspire + Qdrant + Azure OpenAI             │
│  Demonstrates: pipeline composition, RAPTOR, CRAG, Self-RAG │
└─────────────────────────────────────────────────────────────┘
```

Each repo produces local NuGet packages consumed by the next. No cross-repo `ProjectReference` — each repo is self-contained and independently buildable.

## Trade-offs and Honest Assessment

| Consideration | Assessment |
|---------------|-----------|
| **Latency** | Every processor adds an LLM round-trip. Query expansion + reranking + CRAG = 3 additional calls per query. For interactive chat, this adds 2-5 seconds. Ingestion processors are one-time cost. |
| **Cost** | More LLM calls = more tokens = more cost. Tree indexing and hypothetical query generation are especially token-heavy during ingestion. But ingestion is amortized across all queries. |
| **Complexity** | More moving parts. The fluent builder pattern manages this well — each `.UseX()` is independently understandable — but debugging a 6-processor pipeline requires observability (OpenTelemetry traces are built in). |
| **Diminishing returns** | Not every corpus needs every processor. Topic classification on a single-topic corpus is wasted. CRAG on high-quality curated content adds latency without value. The composable design lets you measure and remove what doesn't help. |

**Why the trade-offs are worth it:** Enterprise RAG applications live or die on answer quality. A wrong answer that the user trusts is worse than a slow answer that's correct. Every processor in this pipeline exists to push the quality needle — and the composable design means you only pay for what you use.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/) (for Qdrant vector store)
- An Azure OpenAI resource with:
  - A vision-capable chat model deployment (e.g., `gpt-4o`, `gpt-4.1`, `gpt-5.1`)
  - An embedding model deployment (e.g., `text-embedding-3-small`)

### Configuration

Set user secrets for the AppHost project:

```bash
cd AdvancedRag.AppHost
dotnet user-secrets set "Azure:SubscriptionId" "<your-subscription-id>"
dotnet user-secrets set "Azure:Location" "<your-location>"
dotnet user-secrets set "AzureOpenAI:Name" "<your-azure-openai-resource-name>"
dotnet user-secrets set "AzureOpenAI:ResourceGroup" "<your-resource-group>"
```

### Run

```bash
dotnet run --project AdvancedRag.AppHost
```

### Validate the Pipelines

The repo includes standalone validation apps for testing each pipeline stage:

```bash
# Test ingestion pipeline stages
dotnet run ValidateIngestion.cs

# Test retrieval pipeline: baseline vs. enhanced
dotnet run ValidateRetrieval.cs
```

## What's Next

- **Benchmarking**: Systematic A/B comparison of retrieval accuracy across configurations using the MEAI Evaluation framework
- **Streaming generation**: Self-RAG with streaming output and in-band quality signals
- **Cross-encoder reranking**: ONNX-based local reranking (no LLM round-trip) via MEDIExtensions.Onnx
- **Hybrid search**: Combine vector similarity with BM25 keyword search for better recall on specific terms
- **Adaptive pipeline selection**: Runtime profiling to auto-select the optimal processor combination per query
- **Package publication**: When the upstream fork abstractions merge and MEDIExtensions ships to NuGet, the `local-packages` workaround goes away

The core premise: **RAG quality is a pipeline problem, not a model problem.** Better models help, but composable pre-processing, post-processing, and quality feedback loops are what turn a demo into a production system. Building these as `.UseX()` middleware — familiar to any .NET developer — makes advanced RAG accessible without a PhD in information retrieval.
