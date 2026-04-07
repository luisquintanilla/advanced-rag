using AdvancedRag.Web.Services.Ingestion;
using MEDIExtensions.Retrieval;
using Microsoft.Extensions.VectorData;

namespace AdvancedRag.Web.Services;

public class SemanticSearch(
    VectorStoreCollection<Guid, IngestedChunk> vectorCollection,
    [FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory,
    DataIngestor dataIngestor,
    RetrievalPipeline retrievalPipeline)
{
    private Task? _ingestionTask;

    public async Task LoadDocumentsAsync() => await ( _ingestionTask ??= dataIngestor.IngestDataAsync(ingestionDirectory, searchPattern: "*.*"));

    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        // Ensure documents have been loaded before searching
        await LoadDocumentsAsync();

        // Use RetrievalPipeline for pre-query processing, search, and post-search processing
        var results = await retrievalPipeline.RetrieveAsync(
            vectorCollection,
            text,
            topK: maxResults,
            contentSelector: chunk => chunk.Text,
            cancellationToken: default);

        LastRetrievalMetadata = results.Metadata;

        // Map RetrievalChunks back to IngestedChunk records
        var chunks = results.Chunks
            .Select(c =>
            {
                c.Record.TryGetValue(nameof(IngestedChunk.Key), out var keyObj);
                c.Record.TryGetValue(nameof(IngestedChunk.DocumentId), out var docIdObj);
                return new IngestedChunk
                {
                    Key = keyObj is Guid key ? key : Guid.Empty,
                    DocumentId = docIdObj?.ToString() ?? "",
                    Text = c.Content
                };
            })
            .Where(c => documentIdFilter is not { Length: > 0 } || c.DocumentId == documentIdFilter)
            .ToList();

        return chunks;
    }

    /// <summary>
    /// Pipeline metadata from the last retrieval (e.g., CRAG score, reranking info).
    /// </summary>
    public IDictionary<string, object?>? LastRetrievalMetadata { get; private set; }
}
