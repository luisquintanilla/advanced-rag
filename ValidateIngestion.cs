#:package UglyToad.PdfPig.DataIngestion@0.1.0
#:package Microsoft.Extensions.AI@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.AI.OpenAI@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.DataIngestion@10.5.0-preview.1.26181.4
#:package Microsoft.Extensions.DataIngestion.Markdig@10.5.0-preview.1.26181.4
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.20.0
#:package Microsoft.ML.Tokenizers.Data.O200kBase@1.0.1

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;
using UglyToad.PdfPig.DataIngestion;
using UglyToad.PdfPig.DataIngestion.Processors;

// ─── Configuration ──────────────────────────────────────────────────────────
var pdfPath = Path.Combine("AdvancedRag.Web", "wwwroot", "Data", "Example_Emergency_Survival_Kit.pdf");
var outputDir = Path.Combine("validation-output");
var chatDeployment = "chat";
var embeddingDeployment = "embedding";

Directory.CreateDirectory(outputDir);

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║       AdvancedRag — Ingestion Pipeline Validator         ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ─── Stage 1: PDF Reading (no LLM needed) ──────────────────────────────────
Console.WriteLine("━━━ Stage 1: PDF Reading (PdfPigReader VisionOnly) ━━━");
IngestionDocument? document = null;
try
{
    if (!File.Exists(pdfPath))
    {
        Console.WriteLine($"  ✗ PDF not found: {pdfPath}");
        Console.WriteLine("    Run from C:\\Dev\\AdvancedRag\\ directory.");
        return 1;
    }

    var reader = new PdfPigReader(mode: PdfReadingMode.VisionOnly);
    using var pdfStream = File.OpenRead(pdfPath);
    document = await reader.ReadAsync(pdfStream, "test.pdf", "application/pdf");

    Console.WriteLine($"  ✓ Document read successfully");
    Console.WriteLine($"    Sections: {document.Sections.Count}");

    int pagesWithImage = 0;
    int placeholderCount = 0;

    foreach (var section in document.Sections)
    {
        bool hasImage = section.HasMetadata &&
                        section.Metadata.TryGetValue("page_image", out var imgObj) &&
                        imgObj is byte[];
        if (hasImage) pagesWithImage++;

        foreach (var element in section.Elements)
        {
            bool isPlaceholder = element.HasMetadata && element.Metadata.ContainsKey("placeholder");
            if (isPlaceholder) placeholderCount++;

            var textPreview = string.IsNullOrEmpty(element.Text)
                ? "(empty)"
                : element.Text[..Math.Min(60, element.Text.Length)] + "...";

            Console.WriteLine($"    Page {section.PageNumber}: placeholder={isPlaceholder}, text='{textPreview}'");
        }

        // Save first page image to disk for visual inspection
        if (hasImage && pagesWithImage == 1)
        {
            var imageBytes = (byte[])section.Metadata["page_image"];
            var imagePath = Path.Combine(outputDir, "page1_rendered.png");
            await File.WriteAllBytesAsync(imagePath, imageBytes);
            Console.WriteLine($"  ✓ Page 1 image saved: {imagePath} ({imageBytes.Length:N0} bytes)");
        }
    }

    Console.WriteLine($"  Summary: {document.Sections.Count} pages, {pagesWithImage} with images, {placeholderCount} placeholders");

    if (pagesWithImage == 0)
        Console.WriteLine("  ✗ FAIL: No page images rendered — VisionOcrEnricher will have nothing to OCR!");
    else if (placeholderCount == 0)
        Console.WriteLine("  ✗ FAIL: No placeholder elements — VisionOcrEnricher won't process any elements!");
    else
        Console.WriteLine("  ✓ PASS: Pages have images and placeholder elements");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ FAIL: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();

// ─── Resolve Azure OpenAI ──────────────────────────────────────────────────
Console.WriteLine("━━━ Connecting to Azure OpenAI ━━━");
IChatClient? chatClient = null;
IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null;

try
{
    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
    if (string.IsNullOrEmpty(endpoint))
    {
        Console.WriteLine("  ✗ AZURE_OPENAI_ENDPOINT not set. Skipping LLM stages.");
        Console.WriteLine("    Set it: set AZURE_OPENAI_ENDPOINT=https://<resource>.openai.azure.com/");
        Console.WriteLine();
        Console.WriteLine("━━━ Stages 2-4 skipped (no LLM connection) ━━━");
        PrintSummary(stagesRun: 1);
        return 0;
    }

    var azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    chatClient = azureClient.GetChatClient(chatDeployment).AsIChatClient();
    embeddingGenerator = azureClient.GetEmbeddingClient(embeddingDeployment).AsIEmbeddingGenerator();

    // Quick health check
    var healthResponse = await chatClient.GetResponseAsync("Say 'ok'");
    Console.WriteLine($"  ✓ Chat client connected (deployment: {chatDeployment})");
    Console.WriteLine($"    Health check response: {healthResponse.Text?.Trim()}");
    Console.WriteLine($"  ✓ Embedding client connected (deployment: {embeddingDeployment})");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ FAIL connecting to Azure OpenAI: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("    Skipping LLM stages.");
    PrintSummary(stagesRun: 1);
    return 1;
}

Console.WriteLine();

// ─── Stage 2: VisionOcrEnricher ────────────────────────────────────────────
Console.WriteLine("━━━ Stage 2: VisionOcrEnricher (LLM vision OCR) ━━━");
if (document is not null)
{
    try
    {
        var ocrEnricher = new VisionOcrEnricher(chatClient);
        document = await ocrEnricher.ProcessAsync(document, CancellationToken.None);

        var ocrCount = document.EnumerateContent()
            .Count(e => e.HasMetadata && e.Metadata.ContainsKey("ocr_source"));

        foreach (var section in document.Sections)
        {
            foreach (var element in section.Elements)
            {
                bool wasEnriched = element.HasMetadata && element.Metadata.ContainsKey("ocr_source");
                var textLen = element.Text?.Length ?? 0;
                var textPreview = textLen > 0
                    ? element.Text![..Math.Min(80, textLen)] + "..."
                    : "(still empty)";
                Console.WriteLine($"    Page {section.PageNumber}: ocr={wasEnriched}, chars={textLen}, text='{textPreview}'");
            }
        }

        if (ocrCount > 0)
            Console.WriteLine($"  ✓ PASS: {ocrCount} elements enriched with vision OCR");
        else
            Console.WriteLine("  ✗ FAIL: No elements were enriched — VisionOcrEnricher may not be working");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ FAIL: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();

// ─── Stage 3: SemanticSimilarityChunker (actual chunking) ──────────────────
Console.WriteLine("━━━ Stage 3: SemanticSimilarityChunker (real chunking) ━━━");
List<IngestionChunk<string>>? chunks = null;
if (document is not null && embeddingGenerator is not null)
{
    try
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        var chunkerOptions = new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = 2000,
            OverlapTokens = 0
        };
        var chunker = new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);

        // The chunker operates on the full document via the pipeline.
        // To test standalone, we feed it the document text as chunks.
        // First, let's see what the document looks like to the chunker:
        var elements = document.EnumerateContent()
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .ToList();

        Console.WriteLine($"  Document elements with text: {elements.Count}");
        int totalChars = elements.Sum(e => e.Text!.Length);
        int totalTokens = elements.Sum(e => tokenizer.CountTokens(e.Text!));
        Console.WriteLine($"  Total characters: {totalChars:N0}");
        Console.WriteLine($"  Total tokens: {totalTokens:N0}");
        Console.WriteLine($"  Avg tokens/element: {(elements.Count > 0 ? totalTokens / elements.Count : 0):N0}");
        Console.WriteLine();

        // Show per-element token counts to diagnose chunk granularity
        foreach (var element in elements)
        {
            var tokens = tokenizer.CountTokens(element.Text!);
            Console.WriteLine($"    Page {element.PageNumber}: {tokens} tokens, {element.Text!.Length} chars");
        }

        Console.WriteLine();

        // Now run the actual chunker via the pipeline's internal flow
        // The IngestionPipeline calls chunker internally, so we simulate that:
        chunks = [];
        await foreach (var chunk in chunker.ProcessAsync(document, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Console.WriteLine($"  Chunks produced: {chunks.Count} (from {elements.Count} elements)");
        Console.WriteLine($"  Ratio: {(elements.Count > 0 ? (double)chunks.Count / elements.Count : 0):F1}x elements");

        foreach (var chunk in chunks.Take(8))
        {
            var preview = chunk.Content[..Math.Min(70, chunk.Content.Length)];
            var tokens = tokenizer.CountTokens(chunk.Content);
            Console.WriteLine($"    Chunk: {tokens} tokens, '{preview}...'");
        }
        if (chunks.Count > 8)
            Console.WriteLine($"    ... and {chunks.Count - 8} more chunks");

        // SemanticSimilarityChunker groups elements by semantic similarity, so fewer chunks
        // than elements is expected. The key check is that chunk content is real text, not placeholders.
        bool hasRealContent = chunks.All(c => c.Content.Length > 20 && !c.Content.Contains("[scanned-page]"));
        if (hasRealContent && chunks.Count > 0)
            Console.WriteLine($"  ✓ PASS: {chunks.Count} semantic chunks from {elements.Count} elements — chunks contain real OCR text");
        else if (chunks.Count == 0)
            Console.WriteLine($"  ✗ FAIL: No chunks produced");
        else
            Console.WriteLine($"  ✗ FAIL: Chunks contain placeholder text instead of OCR content");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ FAIL: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine($"    {ex.StackTrace?.Split('\n').FirstOrDefault()}");
    }
}

Console.WriteLine();

// ─── Stage 4: ContextualChunkEnricher ──────────────────────────────────────
Console.WriteLine("━━━ Stage 4: ContextualChunkEnricher (LLM summaries) ━━━");
if (chunks is { Count: > 0 } && chatClient is not null)
{
    try
    {
        var contextEnricher = new ContextualChunkEnricher(chatClient);
        var enrichedChunks = new List<IngestionChunk<string>>();
        await foreach (var chunk in contextEnricher.ProcessAsync(ToAsync(chunks.Take(3))))
        {
            enrichedChunks.Add(chunk);
        }

        int withSummary = enrichedChunks.Count(c => c.HasMetadata && c.Metadata.ContainsKey("contextual_summary"));

        foreach (var chunk in enrichedChunks)
        {
            var contentPreview = chunk.Content[..Math.Min(60, chunk.Content.Length)];
            var summary = chunk.HasMetadata && chunk.Metadata.TryGetValue("contextual_summary", out var s) ? s : "(none)";
            Console.WriteLine($"    [{chunk.Context}] \"{contentPreview}...\"");
            Console.WriteLine($"      Summary: {summary}");
        }

        if (withSummary > 0)
            Console.WriteLine($"  ✓ PASS: {withSummary}/{enrichedChunks.Count} chunks have contextual summaries");
        else
            Console.WriteLine("  ✗ FAIL: No chunks received contextual summaries");
        Console.WriteLine($"  (tested on first 3 chunks only to save LLM calls)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ FAIL: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine();
PrintSummary(stagesRun: 4);
return 0;

// ─── Helpers ───────────────────────────────────────────────────────────────
static void PrintSummary(int stagesRun)
{
    Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
    Console.WriteLine($"║  Validation complete — {stagesRun} stage(s) executed              ║");
    Console.WriteLine("║  Check output above for ✓ PASS / ✗ FAIL per stage      ║");
    Console.WriteLine("║  Page images saved to: validation-output/              ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
}

static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
{
    foreach (var item in source)
    {
        yield return item;
    }
    await Task.CompletedTask;
}
