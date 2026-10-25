using System.Text.Json;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// <see cref="IBookCatalog"/> over Open Library search.json. A typed HttpClient behind the resilience pipeline (see
/// <see cref="OpenLibraryResilience"/>); this class adds the cache, the slow-empty rule, the call budget and the
/// translation of failures into <see cref="CatalogException"/>s.
/// </summary>
internal sealed class OpenLibraryCatalog(HttpClient http, HybridCache cache, IOptions<OpenLibraryOptions> options, TimeProvider time)
    : IBookCatalog
{
    private readonly OpenLibraryOptions _options = options.Value;

    public async Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        var url = OpenLibraryUrls.Search(query)
            ?? throw new CatalogQueryRejectedException("The query has no search terms, so nothing was sent.", 0);

        var fetch = new Fetch(query, url);
        SearchPage page;
        try
        {
            page = _options.SearchCacheDuration > TimeSpan.Zero ? await CachedAsync(fetch, ct) : await FetchAsync(fetch, ct);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested && ex is not CatalogCancelledException)
        {
            // Still a cancellation, but one that says how many requests went out before it.
            throw new CatalogCancelledException(fetch.Attempts, ex, ct);
        }

        var stats = new CatalogCallStats(
            CacheHit: !fetch.Ran,
            Attempts: fetch.Attempts,
            ElapsedMs: (long)time.GetElapsedTime(started).TotalMilliseconds,
            SlowEmptyRetried: page.SlowEmptyRetried);
        return new CatalogResponse(page.Works, page.NumFound, stats);
    }

    private async Task<SearchPage> CachedAsync(Fetch fetch, CancellationToken ct)
    {
        var key = OpenLibraryUrls.CacheKey(new Uri(http.BaseAddress!, fetch.Url));
        try
        {
            // Identical concurrent searches share one fetch (SY-20). A failure is an exception, so it is never cached.
            return await cache.GetOrCreateAsync(key, (Catalog: this, Fetch: fetch),
                static (s, token) => s.Catalog.FetchCacheableAsync(s.Fetch, token), Entry(_options.SearchCacheDuration),
                cancellationToken: ct);
        }
        catch (UncachedPage uncached)
        {
            // The cache fixes entry options before the factory runs, so a page that needs another lifetime leaves
            // the factory as this exception (callers that joined the fetch get it too) and the caller that fetched
            // stores it itself.
            if (fetch.Ran && uncached.CacheFor is { } ttl)
                await cache.SetAsync(key, uncached.Page, Entry(ttl), cancellationToken: CancellationToken.None);
            return uncached.Page;
        }
        catch (CatalogException failure) when (!fetch.Ran)
        {
            // Joined another caller's fetch: its requests were not sent by this call.
            throw WithAttempts(failure, 0);
        }
    }

    private async ValueTask<SearchPage> FetchCacheableAsync(Fetch fetch, CancellationToken ct)
    {
        var page = await FetchAsync(fetch, ct);
        if (page.SlowEmptyRetried) throw new UncachedPage(page, null); // IN-064: never cache a suspect answer
        if (page.Works.Count == 0)
        {
            var ttl = _options.EmptySearchCacheDuration;
            throw new UncachedPage(page, ttl > TimeSpan.Zero ? ttl : null);
        }
        return page;
    }

    private async Task<SearchPage> FetchAsync(Fetch fetch, CancellationToken ct)
    {
        var call = fetch.Start(time.GetTimestamp());
        // One budget for the whole logical call, the slow-empty re-query included, so a call never outlives
        // TotalTimeout and the request deadline can plan on it (SY-06).
        using var budget = new CancellationTokenSource(_options.TotalTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            SearchPage page;
            try
            {
                page = await SendAsync(fetch.Url, call, linked.Token);
            }
            catch (TimeoutRejectedException) when (!linked.IsCancellationRequested)
            {
                call.RetriesAllowed = false;
                var lighter = await SendAsync(OpenLibraryUrls.Search(fetch.Query, slowEmptyRequery: true)!, call, linked.Token);
                if (lighter.Works.Count == 0)
                    throw new CatalogTimeoutException("Open Library timed out and the lighter re-query found no results.", call.Attempts);
                return lighter with { SlowEmptyRetried = true };
            }
            if (page.Works.Count > 0 || call.LastAnsweredAttempt <= _options.SlowEmptyThreshold) return page;

            // IN-064/DA-064: a slow 200 with 0 hits is usually Solr's time-allowed cut-off (`king` returned 0 after
            // 11 s, then 295,758 hits on a retry). Ask once more, lighter and without retries; cache neither answer.
            call.RetriesAllowed = false;
            var again = await SendAsync(OpenLibraryUrls.Search(fetch.Query, slowEmptyRequery: true)!, call, linked.Token);
            if (again.Works.Count == 0)
            {
                // Still nothing: report the cut-off as the timeout it is, never as "no results".
                throw new CatalogTimeoutException(
                    "Open Library answered 0 hits slowly and again on the lighter re-query (time-allowed cut-off).", call.Attempts);
            }
            return again with { SlowEmptyRetried = true };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller gave up: not a catalog failure (SearchAsync adds the attempt count)
        }
        catch (Exception ex) when (Translate(ex, call.Attempts) is { } failure)
        {
            throw failure;
        }
    }

    private async Task<SearchPage> SendAsync(string url, OpenLibraryCall call, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(OpenLibraryCall.Key, call);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await FromStatusAsync(response, call.Attempts, ct);

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<OlSearchResponse>(body, OlSearchResponse.Json, ct)
            ?? throw new CatalogBadResponseException("Open Library returned an empty JSON document.", call.Attempts);
        return OpenLibraryMapper.ToPage(dto, time.GetUtcNow().Year);
    }

    /// <summary>A final non-success status, after the pipeline's retries.</summary>
    private async Task<CatalogException> FromStatusAsync(HttpResponseMessage response, int attempts, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        return status switch
        {
            // IN-005/IN-050/DE-05: 422 "Query too short" (q under 3 characters, a bare `*` or `OR`) is about the
            // query, not the service, so the pipeline re-plans to title= instead of failing. 414 (URI too long) likewise.
            422 or 414 => new CatalogQueryRejectedException(
                $"Open Library rejected the query (HTTP {status}){await ReasonAsync(response, ct)}.", attempts),
            408 or 429 or >= 500 => new CatalogUnavailableException(
                $"Open Library is unavailable (HTTP {status}).", attempts, OpenLibraryResilience.RetryAfter(response, time)),
            _ => new CatalogBadResponseException($"Open Library answered HTTP {status}.", attempts), // 3xx included: redirects are not followed
        };
    }

    /// <summary>
    /// Open Library's own words for a rejection. Its 422 body looks like
    /// {"detail":[{"msg":"Value error, Query too short, must be at least 3 characters","input":"ab",…}]}. Only "msg"
    /// is used, never "input" (the user's text).
    /// </summary>
    private static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var reason = root.ValueKind != JsonValueKind.Object ? null
                : root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Array && detail.GetArrayLength() > 0
                    && detail[0].ValueKind == JsonValueKind.Object && detail[0].TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String
                    ? msg.GetString()
                : root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString()
                : null;
            return string.IsNullOrWhiteSpace(reason) ? "" : ": " + (reason.Length > 200 ? reason[..200] : reason);
        }
        catch (JsonException)
        {
            return ""; // not JSON: the status code says enough
        }
    }

    /// <summary>Exceptions from the handler chain or the body; null for anything unexpected (let it surface).</summary>
    private CatalogException? Translate(Exception ex, int attempts) => ex switch
    {
        CatalogException => null, // already translated (a status code or an empty body)
        // SY-01: our token bucket's queue is full. Nothing was sent; not retried and not a breaker failure.
        RateLimiterRejectedException => new CatalogUnavailableException(
            "Too many Open Library requests are queued.", attempts, TimeSpan.FromSeconds(1), ex),
        BrokenCircuitException => new CatalogUnavailableException(
            "Open Library calls are paused after repeated failures.", attempts, _options.BreakerDuration, ex),
        TimeoutRejectedException or OperationCanceledException => new CatalogTimeoutException(
            $"Open Library did not answer in time ({attempts} attempts).", attempts, ex),
        HttpRequestException => new CatalogUnavailableException("Open Library could not be reached.", attempts, null, ex),
        // Headers said 200, then the connection broke mid-body (HttpIOException is an IOException). The body is read
        // after the pipeline returns, so this is not retried; it is a transient upstream failure, not a server error.
        IOException => new CatalogUnavailableException("Open Library's answer broke off mid-body.", attempts, null, ex),
        JsonException => new CatalogBadResponseException("Open Library returned malformed JSON.", attempts, ex),
        _ => null,
    };

    private static CatalogException WithAttempts(CatalogException ex, int attempts) => ex switch
    {
        CatalogUnavailableException u => new CatalogUnavailableException(u.Message, attempts, u.RetryAfter, u),
        CatalogTimeoutException => new CatalogTimeoutException(ex.Message, attempts, ex),
        CatalogQueryRejectedException => new CatalogQueryRejectedException(ex.Message, attempts, ex),
        _ => new CatalogBadResponseException(ex.Message, attempts, ex),
    };

    private static HybridCacheEntryOptions Entry(TimeSpan ttl) => new() { Expiration = ttl, LocalCacheExpiration = ttl };

    /// <summary>One caller's view of a logical call. <see cref="Ran"/> is false when the cache answered it.</summary>
    private sealed class Fetch(CatalogQuery query, string url)
    {
        public CatalogQuery Query { get; } = query;
        public string Url { get; } = url;
        public OpenLibraryCall? Call { get; private set; }
        public bool Ran => Call is not null;
        public int Attempts => Call?.Attempts ?? 0;

        public OpenLibraryCall Start(long timestamp) => Call = new OpenLibraryCall(timestamp);
    }

    private sealed class UncachedPage(SearchPage page, TimeSpan? cacheFor) : Exception("Search page bypasses the default cache entry.")
    {
        public SearchPage Page { get; } = page;
        public TimeSpan? CacheFor { get; } = cacheFor;
    }
}
