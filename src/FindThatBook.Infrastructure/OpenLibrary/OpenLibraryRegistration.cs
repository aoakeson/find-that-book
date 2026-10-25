using System.Net;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>Registers <see cref="IBookCatalog"/>: typed HttpClient → resilience pipeline → attempt counter → network.</summary>
public static class OpenLibraryRegistration
{
    public static IServiceCollection AddOpenLibrary(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenLibraryOptions>()
            .Bind(configuration.GetSection(OpenLibraryOptions.Section))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpenLibraryOptions>, OpenLibraryOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddHybridCache();
        services.TryAddSingleton(sp => OpenLibraryRateLimiter.Create(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value));
        services.AddTransient<OpenLibraryAttemptCounter>();

        var client = services.AddHttpClient<IBookCatalog, OpenLibraryCatalog>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
            http.BaseAddress = OpenLibraryUrls.BaseAddress(o.BaseUrl);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", o.UserAgent);
            http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline and the call budget own every timeout
        });
        client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // a followed redirect would be a request that is neither throttled nor counted
            AutomaticDecompression = DecompressionMethods.All,
        });
        client.AddResilienceHandler("open-library", (pipeline, context) => OpenLibraryResilience.Build(pipeline,
            context.ServiceProvider.GetRequiredService<IOptions<OpenLibraryOptions>>().Value,
            context.ServiceProvider.GetRequiredService<OpenLibraryRateLimiter>(),
            context.ServiceProvider.GetRequiredService<TimeProvider>(),
            context.ServiceProvider.GetService<UpstreamHealthMonitor>()));
        client.AddHttpMessageHandler<OpenLibraryAttemptCounter>();
        return services;
    }
}
