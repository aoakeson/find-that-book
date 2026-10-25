using FindThatBook.Infrastructure.Health;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// The resilience pipeline, outer to inner:
/// total timeout → retry → circuit breaker → token bucket → attempt timeout → [attempt counter, then the network].
/// It is the standard handler's order with the token bucket added between the breaker and the attempt timeout:
/// every retry waits for a token, and time spent waiting for one costs the call's budget, never attempt time, so a
/// burst queued behind our own limit is not mistaken for a slow upstream (no false timeouts, re-sends or breaker trips).
/// </summary>
internal static class OpenLibraryResilience
{
    public static void Build(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, OpenLibraryOptions o, OpenLibraryRateLimiter limiter,
        TimeProvider time, UpstreamHealthMonitor? healthMonitor = null)
    {
        pipeline
            .AddTimeout(o.TotalTimeout)
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = o.MaxRetries,
                Delay = o.RetryBaseDelay,                    // exponential backoff with jitter (the defaults)
                ShouldRetryAfterHeader = true,               // a Retry-After replaces the backoff delay
                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, args.Context, o, time)),
            })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // Default predicate: 408/429/5xx, HttpRequestException and attempt timeouts. A token-bucket
                // rejection is none of those, so a local burst can't open the breaker (SY-01).
                MinimumThroughput = o.BreakerMinimumThroughput,
                FailureRatio = o.BreakerFailureRatio,
                SamplingDuration = o.BreakerSamplingDuration,
                BreakDuration = o.BreakerDuration,
                OnOpened = args =>
                {
                    healthMonitor?.RecordOpenLibraryCircuitState("open");
                    healthMonitor?.RecordOpenLibraryFailure(
                        args.Outcome.Exception?.GetType().Name ?? "http_error",
                        time.GetUtcNow());
                    return default;
                },
                OnClosed = _ =>
                {
                    healthMonitor?.RecordOpenLibraryCircuitState("closed");
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    healthMonitor?.RecordOpenLibraryCircuitState("half_open");
                    return default;
                },
            })
            .AddRateLimiter(limiter.Limiter)                 // full queue → RateLimiterRejectedException: not retried
            .AddTimeout(o.AttemptTimeout);
    }

    /// <summary>
    /// Transient failures only (408, 429, 5xx, HttpRequestException, attempt timeouts; never 400/404/422), with two
    /// vetoes: the slow-empty re-query is never retried, and a Retry-After we can't afford ends the call.
    /// </summary>
    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context, OpenLibraryOptions o, TimeProvider time)
    {
        if (!HttpClientResiliencePredicates.IsTransient(outcome)) return false;

        var call = OpenLibraryCall.Of(context.GetRequestMessage());
        if (call is { RetriesAllowed: false }) return false;

        // SY-03: sleeping through `Retry-After: 120` would end as a timeout after the whole budget. Wait only when
        // the wait is short and still fits the call's budget; otherwise stop now and report 503 with that Retry-After.
        if (outcome.Result is { } response && RetryAfter(response, time) is { } wait)
        {
            var elapsed = call is null ? TimeSpan.Zero : time.GetElapsedTime(call.StartedTimestamp);
            return wait <= o.MaxRetryAfter && elapsed + wait < o.TotalTimeout;
        }
        return true;
    }

    /// <summary>Retry-After as seconds or an HTTP date (SY-04); null when absent.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, TimeProvider time)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - time.GetUtcNow() : null);
        return wait is null ? null : wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }
}
