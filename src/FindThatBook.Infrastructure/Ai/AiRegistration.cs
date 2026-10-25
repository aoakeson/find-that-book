using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Judge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// AI services. All singletons: they are thread-safe and hold process-wide state (runtime overrides, budget, 429
/// cooldowns, provider health, cached SDK clients). Requires the HybridCache and TimeProvider from the composition root.
/// </summary>
public static class AiRegistration
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAiSettings(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IChatClientFactory, ChatClientFactory>();
        services.TryAddSingleton<AiBudget>();
        services.TryAddSingleton<ProviderHealth>();
        services.TryAddSingleton<AiPricing>();
        services.TryAddSingleton<IAiPricing>(sp => sp.GetRequiredService<AiPricing>());
        services.TryAddSingleton<StructuredLlm>();
        services.TryAddSingleton<IStructuredLlm>(sp => sp.GetRequiredService<StructuredLlm>());
        services.TryAddSingleton<IAiAdmin, AiAdmin>();
        services.TryAddSingleton<IQueryInterpreter, LlmQueryInterpreter>();
        return services;
    }

    /// <summary>Options + the runtime settings store; shared with the judge, so safe to call twice.</summary>
    internal static IServiceCollection AddAiSettings(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(AiSettingsStore))) return services;
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        services.AddOptions<JudgeOptions>().Bind(configuration.GetSection(JudgeOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JudgeOptions>, JudgeOptionsValidator>();
        services.TryAddSingleton(configuration);
        services.AddSingleton<AiSettingsStore>();
        return services;
    }
}
