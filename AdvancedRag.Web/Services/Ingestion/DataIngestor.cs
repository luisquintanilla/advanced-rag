using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using MEDIExtensions.Ingestion;
using UglyToad.PdfPig.DataIngestion.Processors;
using AdvancedRag.Web.Services;

namespace AdvancedRag.Web.Services.Ingestion;

public class DataIngestor(
    ILogger<DataIngestor> logger,
    ILoggerFactory loggerFactory,
    IOptions<IngestionOptions> ingestionOptions,
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient)
{
    public async Task IngestDataAsync(DirectoryInfo directory, string searchPattern)
    {
        var options = ingestionOptions.Value;

        using var writer = new VectorStoreWriter<string>(vectorStore, dimensionCount: IngestedChunk.VectorDimensions, new()
        {
            CollectionName = IngestedChunk.CollectionName,
            DistanceFunction = IngestedChunk.VectorDistanceFunction,
            IncrementalIngestion = false,
        });

        using var pipeline = new IngestionPipeline<string>(
            reader: new DocumentReader(directory),
            chunker: new SemanticSimilarityChunker(embeddingGenerator, new(TiktokenTokenizer.CreateForModel("gpt-4o"))),
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

        if (options.EnableEntityExtraction)
            pipeline.ChunkProcessors.Add(new EntityExtractionProcessor(chatClient));

        if (options.EnableTopicClassification)
            pipeline.ChunkProcessors.Add(new TopicClassificationProcessor(chatClient, options.TopicTaxonomy));

        if (options.EnableHypotheticalQueries)
            pipeline.ChunkProcessors.Add(new HypotheticalQueryProcessor(chatClient));

        if (options.EnableTreeIndex)
            pipeline.ChunkProcessors.Add(new TreeIndexProcessor(chatClient));

        await foreach (var result in pipeline.ProcessAsync(directory, searchPattern))
        {
            logger.LogInformation("Completed processing '{id}'. Succeeded: '{succeeded}'.", result.DocumentId, result.Succeeded);
        }
    }
}
