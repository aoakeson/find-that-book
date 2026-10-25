using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Eval.Replay;
using FindThatBook.Infrastructure;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Eval.Hosting;

public enum CatalogMode { Live, Mirror, Replay }

public sealed record EvalHostOptions
{
    public CatalogMode Catalog { get; init; } = CatalogMode.Live;

    /// <summary>"none" (the LLM-off arm), "gemini", "anthropic" or "openai".</summary>
    public string Provider { get; init; } = "none";
    public string? Model { get; init; }
    public bool Judge { get; init; }

    /// <summary>Replace HybridCache with a pass-through so repeated calls are real calls (stability runs).</summary>
    public bool BypassCaches { get; init; }

    /// <summary>Folders of recorded responses for <see cref="CatalogMode.Replay"/>, in preference order.</summary>
    public IReadOnlyList<string> ReplaySources { get; init; } = [];

    /// <summary>When set, every successful live search.json response is saved here (the record command).</summary>
    public string? RecordTo { get; init; }

    /// <summary>Fail every IHttpClientFactory request (offline tests).</summary>
    public bool BlockNetwork { get; init; }

    /// <summary>Read the repo-root .env and the well-known key names (GEMINI_API_KEY…), as the API does. Off in tests.</summary>
    public bool LoadDotEnv { get; init; } = true;

    public string MirrorUrl { get; init; } = "http://127.0.0.1:8090/";
    public string Environment { get; init; } = "Production";
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;

    /// <summary>Raw configuration overrides (--config Key=Value), applied last.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// Record-on-miss catalog cache folder (live or mirror): requests found there are answered from disk, the rest go
    /// to the backend and are saved there. Keep one folder per backend (never mix mirror and live recordings).
    /// </summary>
    public string? CatalogCache { get; init; }

    /// <summary>Replay interpretations from this file (eval results.jsonl or a research hyp_*.jsonl cache) instead of calling an LLM.</summary>
    public string? Hypotheses { get; init; }

    /// <summary>With <see cref="Hypotheses"/>: interpret queries the file lacks live (same build and prompt), instead of LLM-off.</summary>
    public bool HypothesesFillLive { get; init; }

    /// <summary>"llm" (the configured provider writes sentences) or "template" (never call the explain LLM).</summary>
    public string Explain { get; init; } = "llm";

    /// <summary>Ranker arm: "default" or "baseline" (the measured prototype rules, every addition off), minus the switches in <see cref="RankerOff"/>.</summary>
    public string Ranker { get; init; } = "default";
    public IReadOnlyList<string> RankerOff { get; init; } = [];

    public bool InterpretsWithLlm => (Hypotheses is null || HypothesesFillLive) && !Provider.Equals("none", StringComparison.OrdinalIgnoreCase);
    public bool ExplainsWithLlm => Explain == "llm" && !Provider.Equals("none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The eval's process host: the API's configuration sources (src/FindThatBook.Api appsettings*.json, environment variables)
/// and the API's composition root (<see cref="DependencyInjection.AddFindThatBook"/>), plus only the eval's instruments:
/// the catalog mode and record-on-miss cache, cache bypass, recording, the network guard, and (for comparison arms)
/// replayed hypotheses, template-only explanations and ranker switches. Everything else is exactly what the API runs.
/// </summary>
public sealed class EvalHost : IAsyncDisposable
{
    private readonly IHost _host;

    private EvalHost(IHost host, EvalHostOptions options, ReplayCatalog? replay)
    {
        _host = host;
        Options = options;
        Replay = replay;
    }

    public EvalHostOptions Options { get; }
    public IServiceProvider Services => _host.Services;
    public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

    /// <summary>The replay catalog (replay mode) or the record-on-miss cache (live/mirror with a cache folder).</summary>
    public ReplayCatalog? Replay { get; }
    public HypothesisStore? Hypotheses { get; private init; }

    /// <summary>Every file the recording handler saved in this host (record command, record-on-miss cache).</summary>
    public RecordingSink Recordings { get; private init; } = new();

    /// <summary>The configuration keys the eval sets per mode; listed in eval/README.md.</summary>
    public static IReadOnlyDictionary<string, string?> ModeSettings(EvalHostOptions o)
    {
        var s = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AI:Provider"] = o.Provider,
            ["Judge:Enabled"] = o.Judge ? "true" : "false",
        };
        if (o.Model is not null && ProviderSection(o.Provider) is { } section) s[$"AI:{section}:Model"] = o.Model;
        if (o.Catalog == CatalogMode.Mirror)
        {
            s["OpenLibrary:BaseUrl"] = o.MirrorUrl;
            s["OpenLibrary:RequestsPerSecond"] = "10"; // the adapter's maximum; the 3 req/s politeness limit is for openlibrary.org
            // The mirror's OR fallback can take 10-30 s. Longer timeouts for this mode only (configuration, not code defaults):
            // a timeout here would measure the mirror, not the pipeline. The deadline grows with them so retrieval can finish.
            s["OpenLibrary:AttemptTimeout"] = "00:00:40";
            s["OpenLibrary:TotalTimeout"] = "00:01:30";
            s["OpenLibrary:SlowEmptyThreshold"] = "00:00:35";
            s["OpenLibrary:BreakerSamplingDuration"] = "00:01:30";
            s["Search:RequestDeadline"] = "00:02:00";
        }
        if (o.BypassCaches) s["AI:Cache:Enabled"] = "false";
        if (o.LogLevel > LogLevel.Information)
        {
            s["Logging:LogLevel:Default"] = o.LogLevel.ToString();
            s["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = o.LogLevel.ToString();
        }
        foreach (var (k, v) in o.Settings) s[k] = v;
        return s;
    }

    public static string? ProviderSection(string provider) => provider.ToLowerInvariant() switch
    {
        "gemini" => "Gemini",
        "anthropic" => "Anthropic",
        "openai" => "OpenAI",
        _ => null,
    };

    public static async Task<EvalHost> StartAsync(EvalHostOptions o, CancellationToken ct = default)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = Directory.Exists(RepoPaths.ApiProject) ? RepoPaths.ApiProject : RepoPaths.Root,
            EnvironmentName = o.Environment,
            Args = [],
        });
        // The API's configuration layout (FindThatBookConfiguration): .env and GEMINI_API_KEY-style names sit just below
        // the environment variables; the eval's own mode settings go on top, like command-line arguments.
        if (!o.LoadDotEnv)
            builder.Configuration.AddInMemoryCollection([new(FindThatBookConfiguration.DotEnvEnabledKey, "false"),
                new(FindThatBookConfiguration.EnvironmentAliasesEnabledKey, "false")]);
        builder.Configuration.AddFindThatBookSources(builder.Environment.ContentRootPath);
        if (builder.Environment.IsDevelopment()) AddApiUserSecrets(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(ModeSettings(o));
        builder.Logging.SetMinimumLevel(o.LogLevel);
        builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

        builder.Services.AddFindThatBook(builder.Configuration);

        var services = builder.Services;
        if (o.Ranker != "default" || o.RankerOff.Count > 0)
        {
            // Ablation arm: the same SearchOptions the composition root bound, with other ranker switches.
            var bound = (SearchOptions)services.Last(d => d.ServiceType == typeof(SearchOptions)).ImplementationInstance!;
            services.Replace(ServiceDescriptor.Singleton(bound with { Ranker = RankerArms.Build(o.Ranker, o.RankerOff) }));
        }

        ReplayCatalog? replay = null;
        var recordTo = o.RecordTo;
        var sink = new RecordingSink();
        if (o.Catalog == CatalogMode.Replay)
        {
            replay = new ReplayCatalog(RecordedResponseIndex.Load(o.ReplaySources));
            services.RemoveAll<IBookCatalog>();
            services.AddSingleton<IBookCatalog>(replay);
        }
        else if (o.CatalogCache is { } cache)
        {
            // Record-on-miss: the adapter stays registered as the backend; the replay catalog sits in front of it.
            var adapter = services.Last(d => d.ServiceType == typeof(IBookCatalog));
            var index = RecordedResponseIndex.Load([cache]);
            Func<CatalogQuery, bool>? unsupported = o.Catalog == CatalogMode.Mirror ? MirrorCannotAnswer : null;
            services.RemoveAll<IBookCatalog>();
            services.AddSingleton(sp => new ReplayCatalog(index, () => Create(adapter, sp), unsupported, sink));
            services.AddSingleton<IBookCatalog>(sp => sp.GetRequiredService<ReplayCatalog>());
            recordTo = cache;
        }

        HypothesisStore? store = null;
        if (o.Hypotheses is { } path)
        {
            store = HypothesisStore.Load(path);
            var live = services.LastOrDefault(d => d.ServiceType == typeof(IQueryInterpreter));
            services.RemoveAll<IQueryInterpreter>();
            if (o.HypothesesFillLive && live is not null)
            {
                var replayStore = store;
                services.AddSingleton<IQueryInterpreter>(sp => new ReplayQueryInterpreter(replayStore, () => (IQueryInterpreter)(live.ImplementationFactory?.Invoke(sp)
                    ?? live.ImplementationInstance ?? ActivatorUtilities.CreateInstance(sp, live.ImplementationType!))));
            }
            else services.AddSingleton<IQueryInterpreter>(new ReplayQueryInterpreter(store));
        }
        if (o.Explain == "template")
        {
            services.RemoveAll<IExplanationWriter>();
            services.AddSingleton<IExplanationWriter, TemplateOnlyExplanationWriter>();
        }
        if (o.BypassCaches)
        {
            builder.Services.RemoveAll<HybridCache>();
            builder.Services.AddSingleton<HybridCache, PassThroughHybridCache>();
        }
        if (recordTo is { } folder)
            builder.Services.ConfigureHttpClientDefaults(b => b.AddHttpMessageHandler(() => new RecordingHandler(folder, sink)));
        if (o.BlockNetwork)
            builder.Services.ConfigureHttpClientDefaults(b => b.AddHttpMessageHandler(() => new NetworkBlockedHandler()));

        var host = builder.Build();
        await host.StartAsync(ct);
        replay ??= o.CatalogCache is null ? null : host.Services.GetRequiredService<ReplayCatalog>();
        return new EvalHost(host, o, replay) { Hypotheses = store, Recordings = sink };
    }

    private static IBookCatalog Create(ServiceDescriptor d, IServiceProvider sp) => (IBookCatalog)(d.ImplementationFactory?.Invoke(sp)
        ?? d.ImplementationInstance
        ?? ActivatorUtilities.CreateInstance(sp, d.ImplementationType!));

    /// <summary>
    /// The HTTP mirror has no person index: it treats "person:(a AND b)" as plain words and falls back to a slow OR
    /// search, so it would inject unrelated works. Those calls are answered empty and counted instead.
    /// </summary>
    public static bool MirrorCannotAnswer(CatalogQuery q) => q.Q?.Contains("person:", StringComparison.Ordinal) == true;

    /// <summary>Default record-on-miss folder per backend; under eval/results/*/raw/ so it is gitignored.</summary>
    public static string DefaultCacheFolder(CatalogMode mode) =>
        Path.Combine(RepoPaths.Results, "_catalog-cache", "raw", mode.ToString().ToLowerInvariant());

    /// <summary>
    /// The host's default user secrets belong to the entry assembly (the eval); the API's id is added instead, right
    /// after appsettings.{Environment}.json, where the API's own host puts it.
    /// </summary>
    private static void AddApiUserSecrets(IConfigurationManager configuration)
    {
        var secrets = new ConfigurationBuilder().AddUserSecrets(FindThatBookConfiguration.ApiUserSecretsId).Sources[0];
        var sources = configuration.Sources;
        var json = sources.Select((s, i) => (s, i)).LastOrDefault(x => x.s is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource).i;
        sources.Insert(Math.Min(json + 1, sources.Count), secrets);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
