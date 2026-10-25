using FindThatBook.Core.Explanations;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Search;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Explanations;
using FindThatBook.Infrastructure.Judge;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Infrastructure.Suggest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Infrastructure;

/// <summary>
/// The one composition root shared by the API and the eval tool, so the eval measures exactly what the API runs.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFindThatBook(this IServiceCollection services, IConfiguration configuration)
    {
        EnsureUnicodeNormalization();
        var search = (configuration.GetSection("Search").Get<SearchOptions>() ?? new SearchOptions()).Validated();
        services.AddSingleton(search);
        services.AddSingleton(TimeProvider.System);
        // HybridCache sizes each in-memory entry by its payload bytes, so a size limit bounds total cache memory (SY-26).
        var cacheMegabytes = configuration.GetValue("Cache:SizeLimitMegabytes", 64);
        services.AddMemoryCache(o => o.SizeLimit = cacheMegabytes * 1024L * 1024L);
        services.AddHybridCache(o => o.MaximumPayloadBytes = 1 << 20);

        services.AddOpenLibrary(configuration);       // IBookCatalog: typed HttpClient, resilience, throttle, cache
        services.AddAi(configuration);                // IQueryInterpreter + provider settings, budget, admin store
        services.AddExplanationWriter(configuration); // IExplanationWriter (facts-only LLM sentences)
        services.AddJudge(configuration);             // IMatchJudge: OpenAI Decisions or the null judge
        services.AddSuggest(configuration);           // ISuggestIndex: prebuilt local autocomplete index

        // Pure policy around the adapters, and the orchestrator. Scoped: SearchService depends on typed HttpClients.
        services.AddScoped<InterpretationService>();
        services.AddScoped<ExplanationService>();
        services.AddScoped<SearchService>();
        return services;
    }

    /// <summary>
    /// Accent folding needs ICU. Without it (InvariantGlobalization, or a container image missing libicu)
    /// string.Normalize silently does nothing and "Brontë" would never match "bronte", so fail at startup instead.
    /// </summary>
    private static void EnsureUnicodeNormalization()
    {
        if (TextFolding.Fold("Brontë") != "bronte")
            throw new InvalidOperationException(
                "Unicode normalization is unavailable (ICU missing or InvariantGlobalization enabled); accent folding would silently fail.");
    }
}
