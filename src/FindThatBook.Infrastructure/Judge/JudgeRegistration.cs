using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Infrastructure.Judge;

/// <summary>
/// IMatchJudge: the Decisions client while the judge is enabled (Judge:Enabled or the admin override), otherwise the
/// null judge. Resolved per use (transient), so an admin toggle applies from the next request.
/// </summary>
public static class JudgeRegistration
{
    public static IServiceCollection AddJudge(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAiSettings(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AiPricing>();
        services.TryAddSingleton<IAiPricing>(sp => sp.GetRequiredService<AiPricing>());
        services.TryAddSingleton<JudgeHealth>();
        services.TryAddSingleton<JudgeSpend>();

        // One attempt, bounded by HttpClient.Timeout: no resilience handler, so nothing retries a billed POST.
        var o = configuration.GetSection(JudgeOptions.Section).Get<JudgeOptions>() ?? new JudgeOptions();
        services.AddHttpClient<OpenAiDecisionsJudge>(http =>
        {
            http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
            http.Timeout = TimeSpan.FromMilliseconds(o.TimeoutMs);
        });

        services.AddTransient<IMatchJudge>(sp => sp.GetRequiredService<AiSettingsStore>().JudgeEnabled
            ? sp.GetRequiredService<OpenAiDecisionsJudge>()
            : NullMatchJudge.Instance);
        return services;
    }
}
