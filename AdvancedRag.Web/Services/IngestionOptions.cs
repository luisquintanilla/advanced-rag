namespace AdvancedRag.Web.Services;

public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public bool EnableEntityExtraction { get; set; } = true;
    public bool EnableTopicClassification { get; set; } = true;
    public bool EnableHypotheticalQueries { get; set; }
    public bool EnableTreeIndex { get; set; }
    public string[] TopicTaxonomy { get; set; } = ["web", "data", "performance", "security", "architecture"];
}
