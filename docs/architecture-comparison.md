# Vision-AI Document Processing with PdfPig: A New Direction for .NET RAG Applications

## The Problem: Why Standard PDF Extraction Falls Short

Most enterprise documents live in PDF — and most of those PDFs are *messy*. Scanned contracts, forms with handwritten notes, technical manuals with complex table layouts, reports with watermarks and multi-column text. These are the documents that matter most to a RAG application, and they're exactly the ones that break traditional text extraction.

The standard [`AIChatWeb-CSharp` template](https://github.com/dotnet/extensions/tree/main/src/ProjectTemplates/Microsoft.Extensions.AI.Templates/templates/AIChatWeb-CSharp) from `dotnet/extensions` provides two options for PDF processing:

- **With Aspire**: A [MarkItDown](https://github.com/microsoft/markitdown) MCP server running in Docker, which converts PDFs to text via a server-side HTTP call.
- **Without Aspire**: A basic `PdfPigReader` that uses native PDF text extraction.

Both work well for *digitally-authored PDFs* — documents where text was typed, not scanned. But for the documents that organizations actually need to process — scanned invoices, legacy archives, forms filled out by hand — the result is often empty text, garbled layouts, or missing tables. Garbage in, garbage out.

## The Vision: Let the LLM See the Document

What if, instead of trying to reconstruct text from a PDF's internal structures, we simply **rendered the page as an image and let a vision-capable LLM read it** — the same way a human would?

This is the core insight behind PdfPig's `VisionOnly` reading mode. Every page is rendered as a PNG image. Every image is sent to a vision LLM for OCR. The model sees the page *as it was intended to be seen* — headers, tables, footnotes, watermarks, handwriting — and extracts text with layout awareness that no rule-based parser can match.

PdfPig supports a spectrum of reading modes:

| Mode | How It Works | Best For |
|------|-------------|----------|
| **TextOnly** | Native PDF text extraction | Clean, digitally-authored PDFs |
| **Hybrid** | Native text + page images for scanned pages | Mixed documents |
| **VisionOnly** | All pages rendered as images, no native extraction | Scanned docs, complex layouts, forms |

`VisionOnly` is the most powerful — and the approach explored in this sample.

## Architecture: How It All Fits Together

The template's default pipeline is straightforward:

```
┌──────────┐     ┌──────────────┐     ┌───────────────┐     ┌──────────────┐
│   PDF    │────▶│  MarkItDown  │────▶│ Basic Chunker │────▶│ Vector Store │
│          │     │  MCP Server  │     │               │     │              │
└──────────┘     └──────────────┘     └───────────────┘     └──────────────┘
                   flat text              token-split           embeddings
```

The PdfPig-enhanced pipeline adds **vision-AI processing** at every stage:

```
┌──────────┐     ┌──────────────┐     ┌────────────────┐     ┌──────────────────┐
│   PDF    │────▶│   PdfPig     │────▶│  Vision OCR    │────▶│  Vision Table    │
│          │     │ (page images)│     │  Enricher      │     │  Enricher        │
└──────────┘     └──────────────┘     └────────────────┘     └──────────────────┘
                   PNG per page       LLM reads each page     LLM extracts tables
                                            │                        │
                                            ▼                        ▼
┌──────────────┐     ┌───────────────────┐     ┌──────────────────────────┐
│ Vector Store │◀────│   Contextual      │◀────│  Semantic Similarity     │
│   (Qdrant)   │     │ Chunk Enricher    │     │  Chunker (embeddings)    │
└──────────────┘     └───────────────────┘     └──────────────────────────┘
  enriched chunks     LLM adds summaries       groups by meaning, not size
```

Each stage builds on the previous one:

1. **PdfPigReader** renders every page as a PNG image — no text extraction attempted
2. **VisionOcrEnricher** sends each page image to a vision LLM, replacing placeholders with OCR'd text
3. **VisionTableEnricher** detects tables and extracts them as structured markdown
4. **SemanticSimilarityChunker** groups content by semantic meaning using embeddings (not naive token splitting)
5. **ContextualChunkEnricher** adds a one-sentence summary to each chunk, improving RAG retrieval
6. **VectorStoreWriter** persists enriched, summarized chunks with their embeddings

## The MEDI Framework: Composable Ingestion Pipelines

What makes this approach elegant is that it's built on [`Microsoft.Extensions.DataIngestion`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dataingestion) (MEDI) — a composable pipeline framework where each stage is a pluggable component.

The entire enhanced pipeline is configured in ~15 lines:

```csharp
using var pipeline = new IngestionPipeline<string>(
    reader: new DocumentReader(directory),
    chunker: new SemanticSimilarityChunker(embeddingGenerator,
        new(TiktokenTokenizer.CreateForModel("gpt-4o"))),
    writer: writer,
    loggerFactory: loggerFactory)
{
    DocumentProcessors =
    {
        new VisionOcrEnricher(chatClient),
        new VisionTableEnricher(chatClient)
    },
    ChunkProcessors =
    {
        new ContextualChunkEnricher(chatClient)
    }
};
```

This isn't a monolithic rewrite — it's **additive enhancement** on a standard framework. Want to skip table extraction? Remove one line. Want to add a custom processor for, say, redacting PII before chunking? Implement `IngestionDocumentProcessor` and add it to the list. The pipeline pattern makes experimentation cheap.

## From Ollama to Azure OpenAI: Cloud-Ready AI

The template ships with Ollama — great for local development, but limited in vision capabilities. Vision-AI document processing requires models that can understand images, and that means stepping up to GPT-4o-class models.

This sample uses Azure OpenAI with the [`RunAsExisting()`](https://learn.microsoft.com/dotnet/aspire/azureai/azureai-openai-integration) pattern — referencing a pre-provisioned Azure resource rather than managing lifecycle:

```csharp
// AppHost.cs — reference existing Azure OpenAI resource
var openai = builder.AddAzureOpenAI("openai")
    .RunAsExisting(azOpenAiName, azOpenAiRg);

openai.AddDeployment("chat", "gpt-5.1", "2025-11-13");
openai.AddDeployment("embedding", "text-embedding-3-small", "1");
```

```csharp
// Program.cs — Aspire-managed client registration
var openai = builder.AddAzureOpenAIClient("openai");
openai.AddChatClient("chat")
    .UseFunctionInvocation()
    .UseOpenTelemetry()
    .UseLogging();
openai.AddEmbeddingGenerator("embedding");
```

Because everything flows through `Microsoft.Extensions.AI` abstractions (`IChatClient`, `IEmbeddingGenerator`), swapping Azure OpenAI for another vision-capable provider is a configuration change, not a code rewrite. The PdfPig processors don't know or care which model is behind the `IChatClient` — they just need one that can see images.

## Results: What Changes for RAG Quality

We built a [C# file-based validation app](../ValidateIngestion.cs) (.NET 10) to test each pipeline stage in isolation against a 12-page sample PDF. The results tell the story:

### Before the Fix (VisionOcrEnricher setting only `element.Text`)

| Metric | Value |
|--------|-------|
| Chunks produced | **2** |
| Chunk content | `[scanned-page]` (placeholder text!) |
| Contextual summaries | *"The scanned page appears to contain text that is unreadable"* |

The chunker was seeing placeholder text instead of OCR content — a subtle bug where the MEDI framework's `GetMarkdown()` method reads the element's constructor parameter (`_markdown`), while the enricher was only setting the `Text` property.

### After the Fix (VisionOcrEnricher replacing the element)

| Metric | Value |
|--------|-------|
| Chunks produced | **6** (semantically grouped from 12 pages) |
| Chunk content | Real OCR text (174–5,923 chars per page) |
| Contextual summaries | *"A comprehensive guide to the Life Guard X Emergency Survival Kit, detailing its contents..."* |

The validation app caught what would have been an invisible quality problem in production — chunks that *look* like they're there but contain no useful content.

## Trade-offs and Honest Assessment

This approach is powerful, but it's not free:

| Consideration | Assessment |
|---------------|-----------|
| **Cost** | Every page hits the vision LLM for OCR, plus embedding and enrichment calls. For a 12-page PDF, that's ~15+ LLM round-trips during ingestion. Cost per page is dropping fast, but it's real. |
| **Latency** | Ingestion takes seconds per page, not milliseconds. But this is a *one-time ingestion cost* — queries against the vector store are still fast. |
| **Flexibility** | This sample is Azure OpenAI-only. The template supported 5+ providers. However, the `IChatClient` / `IEmbeddingGenerator` abstractions mean adding providers back is straightforward. |
| **Maturity** | PdfPig's DataIngestion packages are unpublished (local NuGet packages). This is exploratory — a proof of direction, not a production-hardened solution. |

Why the trade-offs are worth it: **scanned documents are the majority of enterprise PDFs**. An RAG app that can't handle scanned content is an RAG app that can't handle reality. The cost of vision-AI processing is a fraction of the cost of bad answers from empty chunks.

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

### Validate the Pipeline

The repo includes a standalone validation app that tests each pipeline stage:

```bash
dotnet run ValidateIngestion.cs
```

This runs 4 stages (PDF reading → Vision OCR → Semantic chunking → Contextual enrichment) and reports pass/fail for each.

## What's Next

- **Package publication**: When PdfPig's DataIngestion packages ship to NuGet, the `local-packages` workaround goes away
- **Hybrid mode**: Use native text extraction when available, vision only for scanned pages — reducing cost without sacrificing quality
- **Multi-model support**: Test with other vision-capable providers (Anthropic Claude, Google Gemini) via the `IChatClient` abstraction
- **Benchmarking**: Systematic comparison of extraction quality and retrieval accuracy across document types (scanned, digital, mixed, forms)
- **Streaming ingestion**: Process documents as they arrive rather than batch-loading at startup

The core premise is simple: **vision models are getting cheaper and better, while the complexity of real-world documents isn't going away.** Building document processing pipelines that leverage vision-AI — through composable, framework-native patterns — positions .NET RAG applications to handle the documents that actually matter.
