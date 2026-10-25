using System.ClientModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Anthropic.Exceptions;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// The one LLM call path: active provider → health/budget/cooldown → HybridCache (key = hash of exactly what is sent,
/// AI-30/SY-27) → SDK call with a hard timeout → finish-reason/refusal/parse checks → usage and spend.
/// Failures are never cached and never thrown: they come back as a status (only caller cancellation propagates).
/// Calls our own guard refuses (budget, cooldown) are <see cref="LlmCallStatus.BudgetExhausted"/>: nothing was sent.
/// </summary>
public sealed partial class StructuredLlm(
    AiSettingsStore settings,
    IChatClientFactory clients,
    AiBudget budget,
    ProviderHealth health,
    AiPricing pricing,
    HybridCache cache,
    TimeProvider time,
    ILogger<StructuredLlm> log) : IStructuredLlm
{
    internal const int MaxPromptChars = 32_000;

    /// <summary>Output records use explicit [JsonPropertyName]s; numbers may arrive as strings from lax providers (AI-14).</summary>
    internal static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private readonly HashSet<string> _warnedNotConfigured = [];

    public Task<LlmResult<T>> CallAsync<T>(StructuredCall call, CancellationToken ct) where T : class =>
        CallAsync<T>(call, settings.ActiveProvider, settings.Options.Cache.Enabled, ct);

    /// <summary>Calls a specific provider (the admin probe uses this with <paramref name="useCache"/> off).</summary>
    internal async Task<LlmResult<T>> CallAsync<T>(StructuredCall call, string provider, bool useCache, CancellationToken ct) where T : class
    {
        var started = time.GetTimestamp();
        var target = settings.Target(provider);
        if (target is null)
        {
            WarnNotConfiguredOnce(provider);
            var detail = provider == AiProviders.None && settings.ProviderSelection == "explicit" ? "provider_none" : "no_api_key";
            return Result<T>(call, provider, settings.ModelFor(provider), LlmCallStatus.NotConfigured, started, detail: detail);
        }
        if (health.DisabledReason(target) is { } disabled)
            return Result<T>(call, target, LlmCallStatus.NotConfigured, started, detail: "disabled: " + disabled);
        if (call.SystemPrompt.Length + call.UserContent.Length > MaxPromptChars)
            return Result<T>(call, target, LlmCallStatus.ProviderError, started, detail: "prompt_too_large");

        var operation = settings.Options.ForOperation(call.Operation);
        var timeout = call.Timeout > TimeSpan.Zero ? call.Timeout : operation.Timeout;
        var maxTokens = Math.Max(call.MaxOutputTokens > 0 ? call.MaxOutputTokens : operation.MaxOutputTokens,
            target.Options.OutputFloor(OperationName(call.Operation)));

        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var attempt = new Attempt<T>(this, call, target, maxTokens);
        try
        {
            CachedAnswer answer;
            if (useCache)
            {
                var ttl = TimeSpan.FromMinutes(settings.Options.Cache.TtlMinutes);
                answer = await cache.GetOrCreateAsync(
                    CacheKey(call, target, SchemaVersion<T>.Hash),
                    attempt,
                    static (a, token) => a.RunAsync(token),
                    new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl },
                    cancellationToken: linked.Token);
            }
            else
            {
                answer = await attempt.RunAsync(linked.Token);
            }

            var value = JsonSerializer.Deserialize<T>(answer.Json, Json);
            if (value is null) return Result<T>(call, target, LlmCallStatus.MalformedOutput, started, detail: "null_json");
            var hit = !attempt.Ran;
            return Result(call, target, LlmCallStatus.Ok, started, value, hit,
                hit ? 0 : answer.InputTokens, hit ? 0 : answer.OutputTokens, hit ? "cache_hit" : null);
        }
        catch (LlmFailure f)
        {
            return Result<T>(call, target, f.Status, started, attempt.InputTokens, attempt.OutputTokens, f.Detail);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<T>(call, target, LlmCallStatus.Timeout, started, attempt.InputTokens, attempt.OutputTokens,
                $"timeout {timeout.TotalMilliseconds:0}ms");
        }
        catch (JsonException)
        {
            return Result<T>(call, target, LlmCallStatus.MalformedOutput, started, detail: "cached_json");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Client construction, options or the cache itself: still a status, never an exception for the search.
            return Result<T>(call, target, LlmCallStatus.ProviderError, started, attempt.InputTokens, attempt.OutputTokens, ex.GetType().Name);
        }
    }

    /// <summary>Bounded key (SY-23): prefix + SHA-256 over every input that shapes the answer, so different prompt
    /// text, provider, model, prompt version or schema can never share an entry (AI-30).</summary>
    internal static string CacheKey(StructuredCall call, ProviderTarget target, string schemaHash)
    {
        var material = string.Join('\u001f', call.Operation, call.PromptId, target.Provider, target.Model, schemaHash,
            call.SystemPrompt, call.UserContent);
        var op = new string(call.Operation.Where(char.IsAsciiLetterOrDigit).Take(16).ToArray());
        return $"ai:{op}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static string OperationName(string operation) =>
        string.Equals(operation, "explain", StringComparison.OrdinalIgnoreCase) ? "Explain" : "Interpret";

    /// <summary>One provider round trip; throws <see cref="LlmFailure"/> so HybridCache never stores a failure.</summary>
    private async Task<CachedAnswer> InvokeAsync<T>(Attempt<T> a, CancellationToken ct) where T : class
    {
        var target = a.Target;
        var gate = budget.TryAcquire(target.Provider, a.Call.Operation);
        if (!gate.Allowed) throw new LlmFailure(gate.Status, gate.Detail);

        a.Ran = true;
        var client = clients.Get(target);
        var options = ProviderChatOptions.Build(target, a.MaxTokens);
        ChatMessage[] messages = [new(ChatRole.System, a.Call.SystemPrompt), new(ChatRole.User, a.Call.UserContent)];
        ChatResponse<T> response;
        try
        {
            response = await client.GetResponseAsync<T>(messages, Json, options, useJsonSchemaResponseFormat: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Fail(target, ex);
        }

        a.InputTokens = response.Usage?.InputTokenCount;
        a.OutputTokens = response.Usage?.OutputTokenCount;
        budget.RecordSpend(pricing.BudgetEstimate(target.Model, a.InputTokens, a.OutputTokens));

        // A cut-off answer is never parsed as a partial hypothesis (AI-01); with thinking on, the cap can go to thoughts.
        if (response.FinishReason == ChatFinishReason.Length)
            throw new LlmFailure(LlmCallStatus.Truncated, "finish=length");
        // Safety stops (Anthropic "refusal", Gemini SAFETY/blocked prompt) and OpenAI's refusal field: no hypothesis (AI-11).
        if (response.FinishReason == ChatFinishReason.ContentFilter
            || response.Messages.SelectMany(m => m.Contents).OfType<ErrorContent>().Any())
            throw new LlmFailure(LlmCallStatus.Refused, $"refusal finish={response.FinishReason?.Value ?? "none"}");
        if (!response.TryGetResult(out var value))
            throw new LlmFailure(LlmCallStatus.MalformedOutput, $"parse finish={response.FinishReason?.Value ?? "none"}");
        return new CachedAnswer(JsonSerializer.Serialize(value, Json), a.InputTokens, a.OutputTokens);
    }

    /// <summary>Maps an SDK exception to a status; 429 starts the cooldown, 401/403/404 switch the target off (AI-55).</summary>
    private LlmFailure Fail(ProviderTarget target, Exception ex)
    {
        var (status, retryAfter) = HttpStatusOf(ex);
        switch (status)
        {
            case 429:
                var cooldown = budget.StartCooldown(target.Provider, retryAfter);
                var (quotaId, quotaValue) = QuotaOf(ex);
                // Quota fields and the delay only: never the key or the request text.
                log.LogWarning("LLM rate limited by {Provider}/{Model}: quota {QuotaId} = {QuotaValue}, retry after {RetryAfterSeconds}s, cooldown {CooldownSeconds}s",
                    target.Provider, target.Model, quotaId ?? "unknown", quotaValue ?? "unknown",
                    retryAfter is { } ra ? Math.Round(ra.TotalSeconds, 1) : null, cooldown.TotalSeconds);
                return new LlmFailure(LlmCallStatus.RateLimited, $"http 429, cooldown {cooldown.TotalSeconds:0}s");
            case 401 or 403:
                health.Disable(target, $"http {status}");
                return new LlmFailure(LlmCallStatus.ProviderError, $"http {status}");
            case 404:
                health.Disable(target, "http 404 (model?)");
                return new LlmFailure(LlmCallStatus.ProviderError, "http 404");
            case 400:
                // A request the API rejects is our bug (schema, option): loud, but still no exception for the user.
                // The redacted excerpt says which option the API rejected (AI-09).
                log.LogError("LLM request rejected by {Provider}/{Model}: HTTP 400 ({ExceptionType}): {Excerpt}",
                    target.Provider, target.Model, ex.GetType().Name, SecretRedaction.Excerpt(ex.Message));
                return new LlmFailure(LlmCallStatus.ProviderError, "http 400");
            case { } code:
                return new LlmFailure(LlmCallStatus.ProviderError, $"http {code}");
        }
        return ex switch
        {
            OperationCanceledException or TimeoutException => new LlmFailure(LlmCallStatus.Timeout, "sdk_timeout"),
            JsonException or InvalidOperationException => new LlmFailure(LlmCallStatus.MalformedOutput, ex.GetType().Name),
            _ => new LlmFailure(LlmCallStatus.ProviderError, ex.GetType().Name),
        };
    }

    /// <summary>HTTP status and Retry-After from each SDK's exception type. Raw messages are never logged (they can echo a masked key, AI-49).</summary>
    internal static (int? Status, TimeSpan? RetryAfter) HttpStatusOf(Exception ex)
    {
        switch (ex)
        {
            case ClientResultException openAi when openAi.Status > 0:
                TimeSpan? after = null;
                if (openAi.GetRawResponse() is { } raw && raw.Headers.TryGetValue("Retry-After", out var header)
                    && double.TryParse(header, System.Globalization.CultureInfo.InvariantCulture, out var s))
                    after = TimeSpan.FromSeconds(s);
                return (openAi.Status, after);
            case AnthropicApiException anthropic:
                return ((int)anthropic.StatusCode, null);
            case Google.GenAI.ApiException gemini when gemini.StatusCode > 0:
                return (gemini.StatusCode, GeminiRetryDelay(gemini.Message));
            case HttpRequestException { StatusCode: { } code }:
                return ((int)code, null);
            default:
                return ex.InnerException is { } inner ? HttpStatusOf(inner) : (null, null);
        }
    }

    /// <summary>
    /// The quota a 429 names, when the provider says: Gemini's google.rpc.QuotaFailure ("quotaId", "quotaValue") in the
    /// message the SDK builds from the error details, OpenAI's x-ratelimit-limit-requests header. Only id-shaped values.
    /// </summary>
    internal static (string? QuotaId, string? QuotaValue) QuotaOf(Exception ex)
    {
        switch (ex)
        {
            case Google.GenAI.ApiException gemini:
                var id = QuotaIdPattern().Match(gemini.Message);
                var value = QuotaValuePattern().Match(gemini.Message);
                return (id.Success ? id.Groups[1].Value : null, value.Success ? value.Groups[1].Value : null);
            case ClientResultException openAi when openAi.GetRawResponse() is { } raw
                && raw.Headers.TryGetValue("x-ratelimit-limit-requests", out var limit) && limit is { Length: <= 12 } && limit.All(char.IsAsciiDigit):
                return ("x-ratelimit-limit-requests", limit);
            default:
                return ex.InnerException is { } inner ? QuotaOf(inner) : (null, null);
        }
    }

    [GeneratedRegex(@"""quotaId""\s*:\s*""([A-Za-z0-9_.\-]{1,100})""")]
    private static partial Regex QuotaIdPattern();

    [GeneratedRegex(@"""quotaValue""\s*:\s*""?([0-9]{1,12})")]
    private static partial Regex QuotaValuePattern();

    /// <summary>Gemini puts google.rpc.RetryInfo in the error details ("retryDelay": "34s"), appended to the message by the SDK.</summary>
    internal static TimeSpan? GeminiRetryDelay(string message)
    {
        var m = RetryDelayPattern().Match(message);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? TimeSpan.FromSeconds(s)
            : null;
    }

    [GeneratedRegex(@"retryDelay\W{0,4}(\d+(?:\.\d+)?)s", RegexOptions.IgnoreCase)]
    private static partial Regex RetryDelayPattern();

    private LlmResult<T> Result<T>(StructuredCall call, ProviderTarget target, LlmCallStatus status, long started,
        long? inputTokens = null, long? outputTokens = null, string? detail = null) where T : class =>
        Result<T>(call, target, status, started, null, false, inputTokens, outputTokens, detail);

    private LlmResult<T> Result<T>(StructuredCall call, string provider, string model, LlmCallStatus status, long started, string? detail)
        where T : class
    {
        var llmCall = new LlmCall(call.Operation, provider, model, call.PromptId, status, false, null, null, ElapsedMs(started), detail);
        Log(llmCall);
        return new LlmResult<T>(null, llmCall);
    }

    private LlmResult<T> Result<T>(StructuredCall call, ProviderTarget target, LlmCallStatus status, long started, T? value,
        bool cacheHit, long? inputTokens, long? outputTokens, string? detail) where T : class
    {
        var llmCall = new LlmCall(call.Operation, target.Provider, target.Model, call.PromptId, status, cacheHit,
            inputTokens, outputTokens, ElapsedMs(started), detail);
        Log(llmCall);
        return new LlmResult<T>(status == LlmCallStatus.Ok ? value : null, llmCall);
    }

    private void Log(LlmCall c)
    {
        var level = c.Status switch
        {
            LlmCallStatus.Ok => LogLevel.Debug,
            LlmCallStatus.NotConfigured => LogLevel.Debug, // warned once at first use
            // Our own guard said no (budget, or the cooldown after a 429): expected, not a fault.
            LlmCallStatus.BudgetExhausted => LogLevel.Information,
            LlmCallStatus.RateLimited => LogLevel.Information, // the 429 already got its own warning with the quota
            _ => LogLevel.Warning,
        };
        log.Log(level, "llm.call {Operation} {Provider}/{Model} {PromptId}: {Status} cache_hit={CacheHit} in={InputTokens} out={OutputTokens} {ElapsedMs}ms {Detail}",
            c.Operation, c.Provider, c.Model, c.PromptId, c.Status, c.CacheHit, c.InputTokens, c.OutputTokens, c.ElapsedMs, c.Detail);
    }

    private void WarnNotConfiguredOnce(string provider)
    {
        lock (_warnedNotConfigured)
            if (!_warnedNotConfigured.Add(provider)) return;
        log.LogWarning("AI provider '{Provider}' is not configured (no API key or 'none'): heuristic interpretation and template explanations (ai_not_configured).", provider);
    }

    private long ElapsedMs(long started) => (long)time.GetElapsedTime(started).TotalMilliseconds;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>State for one call: the factory input, and whether a real provider call happened (cache miss).</summary>
    private sealed class Attempt<T>(StructuredLlm owner, StructuredCall call, ProviderTarget target, int maxTokens) where T : class
    {
        public StructuredCall Call { get; } = call;
        public ProviderTarget Target { get; } = target;
        public int MaxTokens { get; } = maxTokens;
        public bool Ran { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }

        public ValueTask<CachedAnswer> RunAsync(CancellationToken ct) => new(owner.InvokeAsync(this, ct));
    }

    private static class SchemaVersion<T>
    {
        /// <summary>Changes whenever the output record (and so the JSON schema sent) changes.</summary>
        public static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            AIJsonUtilities.CreateJsonSchema(typeof(T), serializerOptions: Json).GetRawText())))[..12];
    }
}

/// <summary>What the cache holds for a successful call: the validated JSON and the usage it cost.</summary>
internal sealed record CachedAnswer(string Json, long? InputTokens, long? OutputTokens);

internal sealed class LlmFailure(LlmCallStatus status, string? detail) : Exception(detail)
{
    public LlmCallStatus Status { get; } = status;
    public string? Detail { get; } = detail;
}
