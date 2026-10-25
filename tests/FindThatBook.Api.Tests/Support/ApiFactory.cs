using System.Net;
using FindThatBook.Api.Endpoints;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Suggest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Api.Tests.Support;

/// <summary>
/// The real API host (Program.cs, real middleware and DI) with every outside dependency faked: no network, no keys.
/// .env and the GEMINI_API_KEY-style environment names are switched off so a developer's real keys never reach a test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<FindThatBook.Api.ApiAssembly>
{
    /// <summary>Header a test sets to play the TCP peer (TestServer reports none). Absent → 127.0.0.1.</summary>
    public const string SocketHeader = "X-Test-Socket-Ip";

    public string EnvironmentName { get; init; } = "Production";
    public Dictionary<string, string?> Settings { get; init; } = [];

    /// <summary>Set to replace the whole pipeline (HTTP-only tests). Null runs the real SearchService over the fakes below.</summary>
    public FakeBookSearch? Search { get; init; }
    public FakeCatalog Catalog { get; } = new();
    public FakeInterpreter Interpreter { get; } = new();
    public FakeExplanationWriter Explainer { get; } = new();
    public FakeJudge Judge { get; } = new();
    public FakeSuggestIndex Suggest { get; } = new();
    public FakeAiAdmin AiAdmin { get; } = new();
    public LogCapture Logs { get; } = new();

    /// <summary>Keep the composition root's IAiAdmin instead of the fake (tests of the real store's masking).</summary>
    public bool RealAiAdmin { get; init; }

    /// <summary>Host the API from another folder (appsettings and the .env search start there).</summary>
    public string? ContentRoot { get; init; }

    /// <summary>Serve static files (an SPA build) from this folder.</summary>
    public string? WebRoot { get; init; }
    public Action<IServiceCollection>? ExtraServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        if (ContentRoot is not null) builder.UseContentRoot(ContentRoot);
        if (WebRoot is not null) builder.UseWebRoot(WebRoot);
        builder.UseSetting("DotEnv:Enabled", "false");
        builder.UseSetting("EnvironmentAliases:Enabled", "false");
        builder.UseSetting("RateLimit:SearchPermitsPerIp", "10000");
        builder.UseSetting("RateLimit:SearchPermitsGlobal", "10000");
        builder.UseSetting("RateLimit:SuggestPermitsPerIp", "10000");
        builder.UseSetting("RateLimit:AdminPermitsPerIp", "10000");
        builder.UseSetting("RateLimit:SearchConcurrency", "1000");
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            Replace<IBookCatalog>(services, Catalog);
            Replace<IQueryInterpreter>(services, Interpreter);
            Replace<IExplanationWriter>(services, Explainer);
            Replace<IMatchJudge>(services, Judge);
            Replace<ISuggestIndex>(services, Suggest);
            Replace<IAiPricing>(services, new FakePricing());
            if (!RealAiAdmin) Replace<IAiAdmin>(services, AiAdmin);
            if (Search is not null)
            {
                services.RemoveAll<IBookSearch>();
                services.AddSingleton<IBookSearch>(Search);
            }
            services.RemoveAll<Microsoft.Extensions.Logging.ILoggerProvider>(); // quiet test output; LogCapture sees everything
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(Logs);
            services.AddSingleton<IStartupFilter, TestSocketPeer>();
            ExtraServices?.Invoke(services);
        });
    }

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Plays the TCP peer before any of Program's middleware runs.</summary>
    private sealed class TestSocketPeer : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                var header = ctx.Request.Headers[SocketHeader].ToString();
                ctx.Connection.RemoteIpAddress = header.Length > 0 ? IPAddress.Parse(header) : IPAddress.Loopback;
                ctx.Request.Headers.Remove(SocketHeader);
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }
}
