using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using MEDIExtensions.Ingestion;
using UglyToad.PdfPig.DataIngestion.Processors;

namespace AdvancedRag.Web.Services.Ingestion;

public class DataIngestor(
    ILogger<DataIngestor> logger,
    ILoggerFactory loggerFactory,
    IConfiguration configuration,
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient)
{
    public async Task IngestDataAsync(DirectoryInfo directory, string searchPattern)
    {
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

        // Add configurable enrichment processors from appsettings.json
        var ingestionConfig = configuration.GetSection("Ingestion");

        if (ingestionConfig.GetValue<bool>("EnableEntityExtraction"))
            pipeline.ChunkProcessors.Add(new EntityExtractionProcessor(chatClient));

        if (ingestionConfig.GetValue<bool>("EnableTopicClassification"))
        {
            var taxonomy = ingestionConfig.GetSection("TopicTaxonomy").Get<string[]>()
                ?? ["web", "data", "performance", "security", "architecture"];
            pipeline.ChunkProcessors.Add(new TopicClassificationProcessor(chatClient, taxonomy));
        }

        if (ingestionConfig.GetValue<bool>("EnableHypotheticalQueries"))
            pipeline.ChunkProcessors.Add(new HypotheticalQueryProcessor(chatClient));

        if (ingestionConfig.GetValue<bool>("EnableTreeIndex"))
            pipeline.ChunkProcessors.Add(new TreeIndexProcessor(chatClient));

        await foreach (var result in pipeline.ProcessAsync(directory, searchPattern))
        {
            logger.LogInformation("Completed processing '{id}'. Succeeded: '{succeeded}'.", result.DocumentId, result.Succeeded);
        }
    }
}
