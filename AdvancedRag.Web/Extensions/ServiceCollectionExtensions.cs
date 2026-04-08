using AdvancedRag.Web.Services;
using MEDIExtensions.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace AdvancedRag.Web.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RetrievalOptions"/>, <see cref="RetrievalPipeline"/>, and any
    /// generation-mode orchestrators configured in the "Retrieval" section.
    /// </summary>
    public static IServiceCollection AddRetrievalPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RetrievalOptions>()
            .BindConfiguration(RetrievalOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RetrievalOptions>>().Value;
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var chatClient = sp.GetRequiredService<IChatClient>();
            var pipeline = new RetrievalPipeline(loggerFactory: loggerFactory);

            ConfigureQueryProcessors(pipeline, options, chatClient);
            ConfigureResultProcessors(pipeline, options, chatClient);

            return pipeline;
        });

        RegisterGenerationOrchestrators(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IngestionOptions"/> bound to the "Ingestion" configuration section.
    /// </summary>
    public static IServiceCollection AddIngestionServices(
        this IServiceCollection services)
    {
        services.AddOptions<IngestionOptions>()
            .BindConfiguration(IngestionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static void ConfigureQueryProcessors(
        RetrievalPipeline pipeline, RetrievalOptions options, IChatClient chatClient)
    {
        switch (options.SearchParadigm)
        {
            case SearchParadigm.Adaptive:
                pipeline.QueryProcessors.Add(new AdaptiveRouter(chatClient));
                break;

            case SearchParadigm.TreeTraversal:
                pipeline.QueryProcessors.Add(new TreeSearchRetriever());
                break;

            case SearchParadigm.Vector:
                switch (options.QueryStrategy)
                {
                    case QueryStrategy.QueryExpansion:
                        pipeline.QueryProcessors.Add(new MultiQueryExpander(chatClient));
                        break;
                    case QueryStrategy.HyDE:
                        pipeline.QueryProcessors.Add(new HydeQueryTransformer(chatClient));
                        break;
                }
                break;
        }
    }

    private static void ConfigureResultProcessors(
        RetrievalPipeline pipeline, RetrievalOptions options, IChatClient chatClient)
    {
        if (options.Reranker == RerankerMode.Llm)
            pipeline.ResultProcessors.Add(new LlmReranker(chatClient));

        if (options.EnableCrag)
            pipeline.ResultProcessors.Add(new CragValidator(chatClient));
    }

    private static void RegisterGenerationOrchestrators(
        IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RetrievalOptions.SectionName).Get<RetrievalOptions>()
            ?? new RetrievalOptions();

        switch (options.GenerationMode)
        {
            case GenerationMode.SelfRag:
                services.AddSingleton(sp =>
                    new SelfRagOrchestrator(
                        sp.GetRequiredService<IChatClient>(),
                        sp.GetService<ILoggerFactory>()));
                break;

            case GenerationMode.SpeculativeRag:
                services.AddSingleton(sp =>
                {
                    var drafter = sp.GetRequiredKeyedService<IChatClient>("drafter");
                    var verifier = sp.GetRequiredService<IChatClient>();
                    return new SpeculativeRagOrchestrator(
                        drafter, verifier, sp.GetService<ILoggerFactory>());
                });
                break;
        }
    }
}
