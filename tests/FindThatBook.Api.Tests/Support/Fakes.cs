using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FindThatBook.Api.Endpoints;
using FindThatBook.Core.Explanations;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Api.Tests.Support;

/// <summary>Stands in for the whole pipeline: returns a hand-built outcome or throws what Open Library would cause.</summary>
public sealed class FakeBookSearch : IBookSearch
{
    public Func<string, int, CancellationToken, Task<SearchOutcome>> Handler { get; set; } =
        (q, _, _) => Task.FromResult(Outcomes.None(q));

    public ConcurrentQueue<(string Query, int Limit)> Calls { get; } = new();

    public Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken ct)
    {
        Calls.Enqueue((query, limit));
        return Handler(query, limit, ct);
    }

    public static FakeBookSearch Returning(SearchOutcome outcome) => new() { Handler = (_, _, _) => Task.FromResult(outcome) };

    public static FakeBookSearch Throwing(Exception ex) => new() { Handler = (_, _, _) => Task.FromException<SearchOutcome>(ex) };
}

/// <summary>Open Library replaced by a function of the query. Counts every call.</summary>
public sealed class FakeCatalog : IBookCatalog
{
    private int _calls;

    public Func<CatalogQuery, CatalogResponse> Respond { get; set; } = _ => new CatalogResponse([], 0, new CatalogCallStats(false, 1, 5));

    public int Calls => _calls;

    public Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Respond(query));
    }
}

public sealed class FakeInterpreter : IQueryInterpreter
{
    private int _calls;

    /// <summary>Null hypothesis + status = the LLM failed (adapters never throw).</summary>
    public Func<CleanedQuery, LlmResult<QueryHypothesis>> Respond { get; set; } = _ => Failed(LlmCallStatus.NotConfigured);

    public int Calls => _calls;

    public Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Respond(query));
    }

    public static LlmResult<QueryHypothesis> Failed(LlmCallStatus status) =>
        new(null, new LlmCall("interpret", "gemini", "test-model", "interpret.v3", status, false, null, null, 3, "fake"));

    public static LlmResult<QueryHypothesis> Ok(QueryHypothesis h, long input = 400, long output = 60) =>
        new(h, new LlmCall("interpret", "gemini", "test-model", "interpret.v3", LlmCallStatus.Ok, false, input, output, 600));
}

public sealed class FakeExplanationWriter : IExplanationWriter
{
    public Func<IReadOnlyList<ExplanationFacts>, LlmResult<IReadOnlyDictionary<string, string>>> Respond { get; set; } =
        _ => new(null, new LlmCall("explain", "gemini", "test-model", "explain.v3", LlmCallStatus.NotConfigured, false, null, null, 1));

    public Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct)
        => Task.FromResult(Respond(facts));
}

public sealed class FakeJudge : IMatchJudge
{
    public bool IsEnabled { get; set; }

    public Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct) =>
        Task.FromResult(new JudgeVerdict(JudgeStatus.Unavailable, Detail: "fake"));
}

/// <summary>Prices "test-model" ($0.30 / $2.50 per million tokens) and the judge's "gpt-6-luna" ($0.10 / $0.50). Anything else is unknown.</summary>
public sealed class FakePricing : IAiPricing
{
    public decimal? Estimate(string model, long? inputTokens, long? outputTokens) => (model, inputTokens, outputTokens) switch
    {
        ("test-model", { } i, { } o) => (i * 0.30m + o * 2.50m) / 1_000_000m,
        ("gpt-6-luna", { } i, { } o) => (i * 0.10m + o * 0.50m) / 1_000_000m,
        _ => null,
    };
}

/// <summary>An in-memory admin store with the same masking contract as the real one.</summary>
public sealed class FakeAiAdmin : IAiAdmin
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _models = new(StringComparer.Ordinal)
    {
        ["gemini"] = "gemini-3.5-flash-lite", ["anthropic"] = "claude-haiku-5-5", ["openai"] = "gpt-4.1-mini",
    };
    private string _provider = "gemini";
    private bool _judge;

    /// <summary>What the probe "provider" says; tests put a key-shaped string in it to prove redaction.</summary>
    public string ProbeDetail { get; set; } = "title=The Hobbit; author=J.R.R. Tolkien";

    public string? LastKeySet { get; private set; }

    public AiStatus GetStatus()
    {
        lock (_gate)
        {
            var providers = _models.Select(m => new ProviderStatus(m.Key, m.Value, m.Key == _provider,
                _keys.ContainsKey(m.Key), _keys.ContainsKey(m.Key) ? "runtime" : "none",
                _keys.TryGetValue(m.Key, out var k) ? Fingerprint(k) : null)).ToList();
            return new AiStatus(_provider, _keys.ContainsKey(_provider), providers,
                new JudgeSettingsStatus(_judge, "gpt-6-luna", _keys.ContainsKey("openai"), "none", null, 0.9), "memory");
        }
    }

    public AiStatus Update(AiSettingsUpdate update)
    {
        lock (_gate)
        {
            if (update.Provider is { } p) _provider = p;
            foreach (var (name, model) in update.Models ?? new Dictionary<string, string>())
            {
                if (!_models.ContainsKey(name)) throw new ArgumentException($"Unknown provider '{name}'.", nameof(update));
                _models[name] = model;
            }
            if (update.JudgeEnabled is { } j) _judge = j;
        }
        return GetStatus();
    }

    public AiStatus SetKey(string provider, string apiKey)
    {
        lock (_gate)
        {
            _keys[provider] = apiKey;
            LastKeySet = apiKey;
        }
        return GetStatus();
    }

    public AiStatus ClearKey(string provider)
    {
        lock (_gate) _keys.Remove(provider);
        return GetStatus();
    }

    public Task<AiProbeResult> ProbeAsync(string provider, CancellationToken ct) =>
        Task.FromResult(new AiProbeResult(provider, _models.GetValueOrDefault(provider, "?"), true, "ok", 412, 380, 40, ProbeDetail));

    private static string Fingerprint(string key) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];
}

public sealed class FakeSuggestIndex : ISuggestIndex
{
    public string Version => "test-1";
    public int Count => 3;
    public (string Prefix, int Limit)? LastLookup { get; private set; }

    public IReadOnlyList<Suggestion> Lookup(string prefix, int limit)
    {
        LastLookup = (prefix, limit);
        if (prefix.Length == 0) return [];
        Suggestion[] all =
        [
            new("OL82563W", "Harry Potter and the Philosopher's Stone", "J. K. Rowling", 1997, 10521536, 337, SuggestMatch.Title),
            new("OL82586W", "Harry Potter and the Deathly Hallows", "J. K. Rowling", 2007, 10110415, 243, SuggestMatch.Title),
            new("OL1168007W", "The Harrowing", "Alexandra Sokoloff", 0, null, 15, SuggestMatch.Word),
        ];
        return [.. all.Take(limit)];
    }
}
