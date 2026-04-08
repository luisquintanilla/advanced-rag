namespace AdvancedRag.Web.Services;

public enum SearchParadigm { Vector, Adaptive, TreeTraversal }
public enum QueryStrategy { None, QueryExpansion, HyDE }
public enum RerankerMode { None, Llm }
public enum GenerationMode { Standard, SelfRag, SpeculativeRag }

public class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public SearchParadigm SearchParadigm { get; set; } = SearchParadigm.Vector;
    public QueryStrategy QueryStrategy { get; set; } = QueryStrategy.None;
    public RerankerMode Reranker { get; set; } = RerankerMode.None;
    public bool EnableCrag { get; set; }
    public GenerationMode GenerationMode { get; set; } = GenerationMode.Standard;
    public string DrafterModel { get; set; } = "chat";
}
