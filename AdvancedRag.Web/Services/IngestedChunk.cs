using System.Text.Json.Serialization;
using Microsoft.Extensions.VectorData;

namespace AdvancedRag.Web.Services;

public class IngestedChunk
{
    public const int VectorDimensions = 1536; // 1536 is the default vector size for the text-embedding-3-small embedding model
    public const string VectorDistanceFunction = DistanceFunction.CosineSimilarity;
    public const string CollectionName = "data-AdvancedRag-chunks";

    [VectorStoreKey(StorageName = "key")]
    [JsonPropertyName("key")]
    public required Guid Key { get; set; }

    [VectorStoreData(StorageName = "documentid")]
    [JsonPropertyName("documentid")]
    public required string DocumentId { get; set; }

    [VectorStoreData(StorageName = "content")]
    [JsonPropertyName("content")]
    public required string Text { get; set; }

    [VectorStoreData(StorageName = "context")]
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    // Entity metadata (populated by EntityExtractionProcessor)
    [VectorStoreData(StorageName = "entities_people")]
    [JsonPropertyName("entities_people")]
    public string? EntitiesPeople { get; set; }

    [VectorStoreData(StorageName = "entities_organizations")]
    [JsonPropertyName("entities_organizations")]
    public string? EntitiesOrganizations { get; set; }

    [VectorStoreData(StorageName = "entities_technologies")]
    [JsonPropertyName("entities_technologies")]
    public string? EntitiesTechnologies { get; set; }

    [VectorStoreData(StorageName = "entities_versions")]
    [JsonPropertyName("entities_versions")]
    public string? EntitiesVersions { get; set; }

    // Topic metadata (populated by TopicClassificationProcessor)
    [VectorStoreData(StorageName = "topic_primary")]
    [JsonPropertyName("topic_primary")]
    public string? TopicPrimary { get; set; }

    [VectorStoreData(StorageName = "topic_secondary")]
    [JsonPropertyName("topic_secondary")]
    public string? TopicSecondary { get; set; }

    // Tree index metadata (populated by TreeIndexWriter)
    [VectorStoreData(StorageName = "level")]
    [JsonPropertyName("level")]
    public int Level { get; set; } = 0; // 0=leaf, 1=branch, 2=root

    [VectorStoreData(StorageName = "parent_id")]
    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    // Hypothetical query metadata (populated by HypotheticalQueryProcessor)
    [VectorStoreData(StorageName = "chunk_type")]
    [JsonPropertyName("chunk_type")]
    public string? ChunkType { get; set; }

    [VectorStoreData(StorageName = "parent_chunk_id")]
    [JsonPropertyName("parent_chunk_id")]
    public string? ParentChunkId { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction, StorageName = "embedding")]
    [JsonPropertyName("embedding")]
    public string? Vector => Text;
}
