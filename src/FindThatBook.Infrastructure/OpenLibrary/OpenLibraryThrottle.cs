using System.Threading.RateLimiting;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// The process-wide token bucket (a singleton: one per process, or it limits nothing). The resilience pipeline takes
/// a token for every attempt, retries included.
/// </summary>
internal sealed class OpenLibraryRateLimiter(RateLimiter limiter) : IDisposable
{
    public RateLimiter Limiter { get; } = limiter;

    public static OpenLibraryRateLimiter Create(OpenLibraryOptions options) => new(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = options.RequestsPerSecond,
        TokensPerPeriod = options.RequestsPerSecond,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = options.QueueLimit,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    }));

    public void Dispose() => Limiter.Dispose();
}

/// <summary>
/// The innermost handler: counts the HTTP requests actually sent and times the one that answered, for
/// <see cref="OpenLibraryCall"/>. It runs after the token was granted, so queue wait is not in the timing.
/// </summary>
internal sealed class OpenLibraryAttemptCounter(TimeProvider time) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = OpenLibraryCall.Of(request);
        call?.AttemptStarted();
        var started = time.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        call?.AttemptAnswered(time.GetElapsedTime(started));
        return response;
    }
}

/// <summary>
/// State of one logical search, carried in <see cref="HttpRequestMessage.Options"/>: the resilience handler re-sends
/// the same request message on a retry, so the attempt counter and the retry predicate both see it.
/// </summary>
internal sealed class OpenLibraryCall(long startedTimestamp)
{
    public static readonly HttpRequestOptionsKey<OpenLibraryCall> Key = new("FindThatBook.OpenLibraryCall");

    private int _attempts;

    /// <summary>HTTP requests sent so far (a call rejected by the token bucket sent none).</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    public long StartedTimestamp { get; } = startedTimestamp;

    /// <summary>False for the slow-empty re-query: it is exactly one attempt.</summary>
    public bool RetriesAllowed { get; set; } = true;

    /// <summary>How long the last attempt that got a response took (to headers).</summary>
    public TimeSpan LastAnsweredAttempt { get; private set; }

    public void AttemptStarted() => Interlocked.Increment(ref _attempts);

    public void AttemptAnswered(TimeSpan duration) => LastAnsweredAttempt = duration;

    public static OpenLibraryCall? Of(HttpRequestMessage? request) =>
        request is not null && request.Options.TryGetValue(Key, out var call) ? call : null;
}
