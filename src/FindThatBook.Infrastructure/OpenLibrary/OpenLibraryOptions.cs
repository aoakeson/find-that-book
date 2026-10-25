using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>Open Library client settings, bound from configuration section "OpenLibrary" and validated at startup.</summary>
public sealed class OpenLibraryOptions
{
    public const string Section = "OpenLibrary";

    /// <summary>search.json host: live Open Library, the local mirror (bulk runs only) or WireMock in tests.</summary>
    public string BaseUrl { get; set; } = "https://openlibrary.org/";

    /// <summary>Site used for links shown to users (BaseUrl may point at a mirror).</summary>
    public string PublicUrl { get; set; } = "https://openlibrary.org/";

    public string CoversUrl { get; set; } = "https://covers.openlibrary.org/";

    /// <summary>Open Library asks API clients to identify themselves with a contact.</summary>
    public string UserAgent { get; set; } = "FindThatBook/0.1 (+https://github.com/aoakeson/find-that-book; andrew@andrewoakeson.com)";

    /// <summary>One token bucket for the whole process; every attempt, retries included, takes a token.</summary>
    public int RequestsPerSecond { get; set; } = 3;

    /// <summary>
    /// Attempts that may wait for a token (3 sending + 20 waiting = 23 calls absorbed by a burst). Beyond that a call
    /// fails fast as unavailable with Retry-After 1 s (SY-01). The longest wait, QueueLimit / RequestsPerSecond
    /// (6.7 s), plus one full attempt must fit in <see cref="TotalTimeout"/>.
    /// </summary>
    public int QueueLimit { get; set; } = 20;

    /// <summary>Cuts the &gt; p90 tail (6.9 s live), not the median (3 s).</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Budget for one logical call: every attempt, retry delay and the slow-empty re-query.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Retries after the first attempt (408, 429, 5xx, connection errors and attempt timeouts only).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>First retry delay; later ones back off exponentially with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Longest Retry-After we wait out. A longer one fails the call at once with that Retry-After (SY-03).</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A 200 with no hits from an attempt slower than this is treated as Solr's time-allowed cut-off and re-asked once
    /// (IN-064/DA-064). Must stay below <see cref="AttemptTimeout"/>, or a slow answer would already be a timeout.
    /// </summary>
    public TimeSpan SlowEmptyThreshold { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>Attempts seen before the breaker may open. At 5, one failed search (3 attempts) could trip it.</summary>
    public int BreakerMinimumThroughput { get; set; } = 10;

    public double BreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Must be at least twice <see cref="AttemptTimeout"/> (the standard handler's own rule).</summary>
    public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BreakerDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a search with hits stays cached. Zero turns the search cache off (live latency runs).</summary>
    public TimeSpan SearchCacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a fast, genuine 0-hit search stays cached: short, because an empty can also be an upstream incident
    /// (SY-28). Zero: never cache empties. Slow empties and failures are never cached.
    /// </summary>
    public TimeSpan EmptySearchCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Worst-case HTTP requests one logical search can send: the first attempt, <see cref="MaxRetries"/> retries and
    /// the single slow-empty re-query (which is never retried). 4 with the defaults.
    /// </summary>
    public int MaxRequestsPerCall => 1 + MaxRetries + 1;
}

/// <summary>Cross-field rules, so a bad config fails at boot rather than on the first search (SY-36).</summary>
internal sealed class OpenLibraryOptionsValidator : IValidateOptions<OpenLibraryOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenLibraryOptions o)
    {
        var errors = new List<string>();
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add($"{OpenLibraryOptions.Section}:{message}");
        }

        Check(IsHttpUrl(o.BaseUrl), "BaseUrl must be an absolute http(s) URL.");
        Check(IsHttpUrl(o.PublicUrl), "PublicUrl must be an absolute http(s) URL.");
        Check(IsHttpUrl(o.CoversUrl), "CoversUrl must be an absolute http(s) URL.");
        Check(!string.IsNullOrWhiteSpace(o.UserAgent), "UserAgent is required (Open Library asks clients to identify themselves).");
        Check(o.RequestsPerSecond is >= 1 and <= 10, "RequestsPerSecond must be 1-10 (Open Library allows about 3).");
        Check(o.QueueLimit is >= 0 and <= 1000, "QueueLimit must be 0-1000.");
        Check(o.RequestsPerSecond < 1 || TimeSpan.FromSeconds((double)o.QueueLimit / o.RequestsPerSecond) + o.AttemptTimeout <= o.TotalTimeout,
            "QueueLimit / RequestsPerSecond (the longest wait for a token) plus AttemptTimeout must fit in TotalTimeout.");
        Check(o.AttemptTimeout >= TimeSpan.FromMilliseconds(10) && o.AttemptTimeout <= TimeSpan.FromMinutes(1),
            "AttemptTimeout must be between 10 ms and 1 minute.");
        Check(o.TotalTimeout > o.AttemptTimeout && o.TotalTimeout <= TimeSpan.FromMinutes(2),
            "TotalTimeout must be longer than AttemptTimeout and at most 2 minutes.");
        Check(o.MaxRetries is >= 1 and <= 5, "MaxRetries must be 1-5.");
        Check(o.RetryBaseDelay >= TimeSpan.Zero && o.RetryBaseDelay <= TimeSpan.FromSeconds(10), "RetryBaseDelay must be 0-10 s.");
        Check(o.MaxRetryAfter >= TimeSpan.Zero && o.MaxRetryAfter < o.TotalTimeout, "MaxRetryAfter must be at least 0 and shorter than TotalTimeout.");
        Check(o.SlowEmptyThreshold > TimeSpan.Zero && o.SlowEmptyThreshold < o.AttemptTimeout,
            "SlowEmptyThreshold must be positive and shorter than AttemptTimeout.");
        Check(o.BreakerMinimumThroughput >= 2, "BreakerMinimumThroughput must be at least 2.");
        Check(o.BreakerFailureRatio is > 0 and <= 1, "BreakerFailureRatio must be in (0, 1].");
        Check(o.BreakerSamplingDuration >= 2 * o.AttemptTimeout && o.BreakerSamplingDuration >= TimeSpan.FromMilliseconds(500),
            "BreakerSamplingDuration must be at least twice AttemptTimeout.");
        Check(o.BreakerDuration >= TimeSpan.FromMilliseconds(500), "BreakerDuration must be at least 500 ms.");
        Check(o.SearchCacheDuration >= TimeSpan.Zero, "SearchCacheDuration must not be negative.");
        Check(o.EmptySearchCacheDuration >= TimeSpan.Zero, "EmptySearchCacheDuration must not be negative.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
