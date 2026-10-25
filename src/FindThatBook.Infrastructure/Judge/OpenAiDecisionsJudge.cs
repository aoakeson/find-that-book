using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Infrastructure.Judge;

/// <summary>
/// OpenAI Decisions as a status gate: one choice question over the deduped candidates, POST /v1/decisions. One attempt
/// within Judge:TimeoutMs (a retry would bill a second request for an answer that only gates status); a 429 starts a
/// short cooldown during which the judge is skipped. Never throws except caller cancellation: failures are
/// <see cref="JudgeStatus.Unavailable"/>, calls it decides not to send are <see cref="JudgeStatus.Skipped"/>.
/// </summary>
public sealed class OpenAiDecisionsJudge(
    HttpClient http,
    AiSettingsStore settings,
    JudgeHealth health,
    JudgeSpend spend,
    AiPricing pricing,
    HybridCache cache,
    TimeProvider time,
    ILogger<OpenAiDecisionsJudge> log) : IMatchJudge
{
    private JudgeOptions Options => settings.JudgeOptions;

    public bool IsEnabled => settings.JudgeEnabled && settings.KeyFor(AiSettingsStore.JudgeKeyName) is { IsSet: true } key
        && health.DisabledReason(Options.Model, key.Fingerprint!) is null;

    public async Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct)
    {
        var key = settings.KeyFor(AiSettingsStore.JudgeKeyName);
        if (!settings.JudgeEnabled || !key.IsSet) return JudgeVerdict.Off;
        var model = Options.Model;
        if (health.DisabledReason(model, key.Fingerprint!) is { } reason) return new JudgeVerdict(JudgeStatus.Unavailable, Model: model, Detail: reason);

        var (body, ids) = DecisionsRequestBuilder.Build(request, Options);
        if (ids.Count < 2) return JudgeVerdict.Skipped("fewer_than_2_candidates"); // the API rejects fewer than 2 choices
        if (health.InCooldown) return JudgeVerdict.Skipped("cooldown");
        if (Options.DailyCostCapUsd > 0 && spend.Today >= Options.DailyCostCapUsd) return JudgeVerdict.Skipped("daily_cap");

        var json = body.ToJsonString();
        var started = time.GetTimestamp();
        if (Options.CacheMinutes <= 0) return await SendAsync(json, ids, model, key, started, ct);

        var state = new Attempt(this, json, ids, model, key, started);
        try
        {
            var ttl = TimeSpan.FromMinutes(Options.CacheMinutes);
            var cached = await cache.GetOrCreateAsync(CacheKey(json), state, static (s, token) => s.RunAsync(token),
                new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl }, cancellationToken: ct);
            return state.Ran ? cached.ToVerdict(ElapsedMs(started)) : cached.ToVerdict(ElapsedMs(started)) with { InputTokens = 0, Detail = "cache_hit" };
        }
        catch (JudgeUnavailable u)
        {
            return u.Verdict;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Judge cache failed: {ExceptionType}", ex.GetType().Name);
            return new JudgeVerdict(JudgeStatus.Unavailable, Model: model, ElapsedMs: ElapsedMs(started), Detail: ex.GetType().Name);
        }
    }

    /// <summary>Hash of the exact body (model + query + candidates in order).</summary>
    internal static string CacheKey(string body) =>
        "judge:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private async Task<JudgeVerdict> SendAsync(string json, IReadOnlyList<string> ids, string model, ResolvedKey key, long started, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/decisions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Value);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                var verdict = DecisionsResponseParser.Parse(text, ids, model, ElapsedMs(started));
                if (verdict.InputTokens is { } tokens) spend.Add(pricing.BudgetEstimate(model, tokens, 0));
                if (verdict.Status == JudgeStatus.Unavailable) log.LogWarning("Judge response unusable: {Detail}", verdict.Detail);
                return verdict;
            }
            return Failed(response.StatusCode, response.Headers.RetryAfter?.Delta, text, model, key, started);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout (Judge:TimeoutMs) elapsed; the caller's own cancellation was rethrown above.
            return new JudgeVerdict(JudgeStatus.Unavailable, Model: model, ElapsedMs: ElapsedMs(started), Detail: "timeout");
        }
        catch (Exception ex)
        {
            log.LogWarning("Judge call failed: {ExceptionType}", ex.GetType().Name);
            return new JudgeVerdict(JudgeStatus.Unavailable, Model: model, ElapsedMs: ElapsedMs(started), Detail: ex.GetType().Name);
        }
    }

    private JudgeVerdict Failed(HttpStatusCode status, TimeSpan? retryAfter, string body, string model, ResolvedKey key, long started)
    {
        var code = (int)status;
        var (errorCode, param) = ErrorFields(body);
        var detail = $"http {code}";
        if (status == HttpStatusCode.TooManyRequests)
        {
            var cooldown = health.StartCooldown(retryAfter, Options.CooldownSeconds, Options.MaxCooldownSeconds);
            log.LogWarning("Judge rate limited: HTTP 429 ({ErrorCode}), cooldown {CooldownSeconds}s", errorCode, cooldown.TotalSeconds);
        }
        else if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // A bad key or unknown model will not fix itself: off for this key+model until restart (AI-49). Log the code only:
            // the message can echo a masked key.
            if (health.Disable(model, key.Fingerprint!, detail))
                log.LogWarning("Judge disabled: HTTP {Status} ({ErrorCode}) for model {Model}", code, errorCode, model);
        }
        else if (status == HttpStatusCode.BadRequest)
        {
            log.LogError("Judge request rejected: HTTP 400 ({ErrorCode}, param {Param}); the request builder needs a fix", errorCode, param);
        }
        return new JudgeVerdict(JudgeStatus.Unavailable, Model: model, ElapsedMs: ElapsedMs(started), Detail: detail);
    }

    private static (string? Code, string? Param) ErrorFields(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object) return (null, null);
            static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Str(e, "code"), Str(e, "param"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private long ElapsedMs(long started) => (long)time.GetElapsedTime(started).TotalMilliseconds;

    private sealed class Attempt(OpenAiDecisionsJudge owner, string json, IReadOnlyList<string> ids, string model, ResolvedKey key, long started)
    {
        public bool Ran { get; private set; }

        public async ValueTask<CachedVerdict> RunAsync(CancellationToken ct)
        {
            Ran = true;
            var verdict = await owner.SendAsync(json, ids, model, key, started, ct);
            // Only real answers are cached; trouble is retried on the next search.
            if (verdict.Status == JudgeStatus.Unavailable) throw new JudgeUnavailable(verdict);
            return CachedVerdict.From(verdict);
        }
    }

    private sealed class JudgeUnavailable(JudgeVerdict verdict) : Exception(verdict.Detail)
    {
        public JudgeVerdict Verdict { get; } = verdict;
    }
}

internal sealed record CachedVerdict(JudgeStatus Status, string? Pick, double? Probability, string? Model, long? InputTokens, string? Detail)
{
    public static CachedVerdict From(JudgeVerdict v) => new(v.Status, v.Pick, v.Probability, v.Model, v.InputTokens, v.Detail);
    public JudgeVerdict ToVerdict(long elapsedMs) => new(Status, Pick, Probability, Model, InputTokens, elapsedMs, Detail);
}

/// <summary>
/// 401/403/404 switch the judge off for that model+key until restart (or a new key from the admin API); a 429 pauses it
/// for Retry-After (bounded) or the configured cooldown.
/// </summary>
public sealed class JudgeHealth(TimeProvider time)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _disabled = new(StringComparer.Ordinal);
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

    public bool InCooldown
    {
        get
        {
            lock (_gate) return time.GetUtcNow() < _cooldownUntil;
        }
    }

    public TimeSpan StartCooldown(TimeSpan? retryAfter, int defaultSeconds, int maxSeconds)
    {
        var seconds = retryAfter is { } ra && ra > TimeSpan.Zero ? ra.TotalSeconds : defaultSeconds;
        var length = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, maxSeconds));
        var until = time.GetUtcNow() + length;
        lock (_gate)
            if (until > _cooldownUntil) _cooldownUntil = until;
        return length;
    }

    /// <returns>True the first time (so the caller logs once).</returns>
    public bool Disable(string model, string fingerprint, string reason)
    {
        lock (_gate) return _disabled.TryAdd($"{model}|{fingerprint}", reason);
    }

    public string? DisabledReason(string model, string fingerprint)
    {
        lock (_gate) return _disabled.GetValueOrDefault($"{model}|{fingerprint}");
    }
}

/// <summary>The judge's estimated spend today (SY-08).</summary>
public sealed class JudgeSpend(TimeProvider time)
{
    private readonly Lock _gate = new();
    private readonly DailyCostMeter _meter = new(time);

    public decimal Today
    {
        get
        {
            lock (_gate) return _meter.Today;
        }
    }

    public void Add(decimal? usd)
    {
        lock (_gate) _meter.Add(usd);
    }
}

/// <summary>The judge when it is switched off: reports Off, never calls anything.</summary>
public sealed class NullMatchJudge : IMatchJudge
{
    public static NullMatchJudge Instance { get; } = new();
    public bool IsEnabled => false;
    public Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct) => Task.FromResult(JudgeVerdict.Off);
}
