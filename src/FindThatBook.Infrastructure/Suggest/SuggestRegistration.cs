using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FindThatBook.Infrastructure.Suggest;

public static class SuggestRegistration
{
    /// <summary>ISuggestIndex = the committed index (singleton, immutable), loaded while the host starts.</summary>
    public static IServiceCollection AddSuggest(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISuggestIndex>(_ => SuggestIndex.Embedded);
        services.AddHostedService<SuggestIndexWarmup>();
        return services;
    }
}

/// <summary>Parses the embedded index at startup (≈ 0.4–0.8 s) so the first keystroke does not pay for it.</summary>
internal sealed class SuggestIndexWarmup(ISuggestIndex index) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = index.Count;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
