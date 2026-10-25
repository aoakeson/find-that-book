using System.Diagnostics;
using System.Text.Json;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Judge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Infrastructure.Tests.Ai;

/// <summary>
/// Opt-in live calls through the real adapters: a provider counts as supported only after this passes.
/// Never runs in CI: explicit, and skipped unless FTB_LIVE_SMOKE=1. Keys come from the process environment
/// (GEMINI_API_KEY / ANTHROPIC_API_KEY / OPENAI_API_KEY) and are never printed.
/// Run: <c>set -a; source .env; set +a; FTB_LIVE_SMOKE=1 FTB_SMOKE_PROVIDER=gemini FTB_SMOKE_QUERIES=1 dotnet test
/// --project tests/FindThatBook.Infrastructure.Tests -- --explicit only --filter-class *LiveSmokeTests</c>.
/// FTB_SMOKE_MODEL overrides the model, FTB_SMOKE_OUT appends one JSON line per call (no keys, no raw responses).
/// </summary>
public sealed class LiveSmokeTests(ITestOutputHelper output)
{
    /// <summary>Golden cases (eval/golden.json) chosen to cover the interpreter's jobs: first name, noise, typos,
    /// characters only, a lone character name, and a translated title.</summary>
    private static readonly string[] Queries =
    [
        "tolkien hobbit illustrated deluxe 1937", // golden brief-03
        "mark huckleberry",                       // golden brief-01
        "great expecttions dikens",               // typo-02
        "scout atticus",                          // character-01
        "hermione",                               // ambig-03
        "the three body problem",                 // DE-06
    ];

    /// <summary>FTB_SMOKE_SET=intent: the author, character and plot queries v4 mislabelled, and topic requests.</summary>
    private static readonly string[] IntentQueries =
    [
        "dickens",
        "hermione",
        "mark twain",
        "the purple elephant who sang opera on mars",
        "find me a book about bears",
        "books like harry potter",
        "un livre sur les ours",
    ];

    /// <summary>FTB_SMOKE_SET=topic: prompt v4's topic intent, plus two real titles that contain topic-ish words.</summary>
    private static readonly string[] TopicQueries =
    [
        "find me a book about bears",
        "books like harry potter",
        "un livre sur les ours",
        "much ado about nothing",
        "the hobbit",
        "recommend me a good book",
    ];

    [Fact(Explicit = true)]
    public async Task Interpret_through_the_real_adapter()
    {
        if (Environment.GetEnvironmentVariable("FTB_LIVE_SMOKE") != "1") Assert.Skip("FTB_LIVE_SMOKE != 1");
        var provider = Environment.GetEnvironmentVariable("FTB_SMOKE_PROVIDER") ?? "gemini";
        if (provider == "judge")
        {
            await JudgeAsync();
            return;
        }
        var count = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_QUERIES"), out var n) ? n : 1;
        var skip = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_SKIP"), out var s) ? s : 0;
        var (section, envKey) = provider switch
        {
            "gemini" => ("Gemini", "GEMINI_API_KEY"),
            "anthropic" => ("Anthropic", "ANTHROPIC_API_KEY"),
            "openai" => ("OpenAI", "OPENAI_API_KEY"),
            _ => throw new ArgumentException(provider),
        };
        var settings = new Dictionary<string, string?>
        {
            ["AI:Provider"] = provider,
            [$"AI:{section}:ApiKey"] = Environment.GetEnvironmentVariable(envKey),
            ["AI:Cache:Enabled"] = "false",
            [$"AI:{section}:RequestsPerMinute"] = "0",
        };
        if (Environment.GetEnvironmentVariable("FTB_SMOKE_MODEL") is { Length: > 0 } model) settings[$"AI:{section}:Model"] = model;

        var recorder = new RecordingFactory(new ChatClientFactory());
        await using var sp = Services(settings, recorder);
        var interpreter = sp.GetRequiredService<IQueryInterpreter>();
        var lines = new List<string>();
        var set = Environment.GetEnvironmentVariable("FTB_SMOKE_SET") switch { "topic" => TopicQueries, "intent" => IntentQueries, _ => Queries };
        foreach (var q in set.Skip(skip).Take(count))
        {
            var sw = Stopwatch.StartNew();
            var result = await interpreter.InterpretAsync(Cleaned(q), TestContext.Current.CancellationToken);
            sw.Stop();
            var last = recorder.Last;
            var line = JsonSerializer.Serialize(new
            {
                provider,
                model = result.Call.Model,
                query = q,
                status = result.Call.Status.ToString(),
                detail = result.Call.Detail,
                finish = last?.FinishReason?.Value,
                ms = sw.ElapsedMilliseconds,
                input_tokens = result.Call.InputTokens,
                output_tokens = result.Call.OutputTokens,
                reasoning_tokens = last?.Usage?.ReasoningTokenCount,
                hypothesis = result.Value,
                sanitized = result.Value is { } raw ? FindThatBook.Core.Interpretation.HypothesisSanitizer.Sanitize(raw, FindThatBook.Core.Normalization.QueryCleaner.Clean(q)) : null,
            });
            output.WriteLine(line);
            lines.Add(line);
            recorder.Last = null;
        }
        if (Environment.GetEnvironmentVariable("FTB_SMOKE_OUT") is { Length: > 0 } path) await File.AppendAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
    }

    /// <summary>Judge calls: the tier-B adapter case, and P&amp;P twins that differ only by edition count, in both orders (AI-39).</summary>
    private async Task JudgeAsync()
    {
        var settings = new Dictionary<string, string?>
        {
            ["AI:Provider"] = "none",
            ["AI:OpenAI:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            ["Judge:Enabled"] = "true",
            ["Judge:CacheMinutes"] = "0",
        };
        await using var sp = Services(settings, null);
        var judge = sp.GetRequiredService<IMatchJudge>();
        judge.IsEnabled.ShouldBeTrue();
        JudgeRequest[] requests =
        [
            new("hobbit dixon graphic novel",
            [
                new("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], ["Alan Lee (Illustrator)"], 1937, 482),
                new("OL219602W", "The Hobbit", ["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"], ["Charles Dixon (Adapter)", "Sean Deming (Adapter)"], 1990, 10),
            ]),
            new("pride and prejudice",
            [
                new("OL21177W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 56),
                new("OL66554W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 4042),
                new("OL3W", "Emma", ["Jane Austen"], [], 1815, 900),
            ]),
            new("pride and prejudice",
            [
                new("OL66554W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 4042),
                new("OL21177W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 56),
                new("OL3W", "Emma", ["Jane Austen"], [], 1815, 900),
            ]),
        ];
        var skip = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_SKIP"), out var s) ? s : 0;
        var count = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_QUERIES"), out var n) ? n : 2;
        var lines = new List<string>();
        foreach (var r in requests.Skip(skip).Take(count))
        {
            var v = await judge.JudgeAsync(r, TestContext.Current.CancellationToken);
            var line = JsonSerializer.Serialize(new { provider = "judge", query = r.Query, status = v.Status.ToString(), v.Pick, v.Probability, v.Model, v.InputTokens, ms = v.ElapsedMs, v.Detail });
            output.WriteLine(line);
            lines.Add(line);
        }
        if (Environment.GetEnvironmentVariable("FTB_SMOKE_OUT") is { Length: > 0 } path) await File.AppendAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings, IChatClientFactory? factory)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton(new FindThatBook.Core.Search.SearchOptions());
        if (factory is not null) services.AddSingleton(factory);
        services.AddAi(configuration);
        services.AddJudge(configuration);
        return services.BuildServiceProvider();
    }

    internal static CleanedQuery Cleaned(string raw) => new()
    {
        Raw = raw,
        Text = raw,
        All = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Search = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Optional = new HashSet<string>(),
        Head = [],
        SoftTokens = new HashSet<string>(),
        Intents = new HashSet<EditionIntent>(),
        RemovedNoise = [],
    };

    /// <summary>Keeps the last raw response so the smoke can report finish reason and the thinking-token split.</summary>
    private sealed class RecordingFactory(IChatClientFactory inner) : IChatClientFactory
    {
        public ChatResponse? Last { get; set; }

        public IChatClient Get(ProviderTarget target) => new Recording(inner.Get(target), this);

        private sealed class Recording(IChatClient inner, RecordingFactory owner) : DelegatingChatClient(inner)
        {
            public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                var response = await base.GetResponseAsync(messages, options, cancellationToken);
                owner.Last = response;
                return response;
            }
        }
    }
}
