using System.Diagnostics;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using H = FindThatBook.Infrastructure.Tests.OpenLibrary.OpenLibraryHarness;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>
/// A burst at the production limits: 3 req/s, 20 queued, 8 s attempt timeout, 15 s budget, a healthy but slow
/// upstream. Waiting for a token must cost budget, not attempt time: no false timeouts, re-sends or breaker trips.
/// </summary>
public sealed class OpenLibraryBurstTests
{
    private static readonly Dictionary<string, string?> Production = new()
    {
        ["RequestsPerSecond"] = "3",
        ["QueueLimit"] = "20",
        ["AttemptTimeout"] = "00:00:08",
        ["TotalTimeout"] = "00:00:15",
        ["RetryBaseDelay"] = "00:00:00.500",
        ["SlowEmptyThreshold"] = "00:00:06",
        ["SearchCacheDuration"] = "00:00:00", // 23 distinct searches anyway; keep the cache out of the measurement
    };

    [Theory]
    [InlineData(4000)]
    [InlineData(6000)]
    public async Task Burst_of_23_calls_with_a_slow_upstream_all_succeed_with_one_request_each(int upstreamMs)
    {
        // 3 go out at once, 20 wait up to ~7 s for tokens, then each takes upstreamMs: the last ends near 7 s + 6 s.
        await using var ol = H.Create(Production);
        ol.Always(Reply.Hits("OL1W").After(upstreamMs));
        var catalog = ol.Catalog;
        var clock = Stopwatch.StartNew();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 23).Select(i => Outcome(catalog, $"burst {i}")));

        outcomes.OfType<string>().ShouldBeEmpty();                                     // no timeouts, no open breaker
        outcomes.OfType<CatalogResponse>().ShouldAllBe(r => r.Stats.Attempts == 1);   // nothing re-sent because of queue wait
        ol.RequestCount.ShouldBe(23);
        TestContext.Current.SendDiagnosticMessage($"burst of 23 at {upstreamMs} ms upstream: all ok in {clock.Elapsed.TotalSeconds:0.0} s, 23 requests");
    }

    /// <summary>The response, or a one-line description of the failure.</summary>
    private static async Task<object> Outcome(IBookCatalog catalog, string q)
    {
        try
        {
            return await catalog.SearchAsync(new CatalogQuery { Q = q }, TestContext.Current.CancellationToken);
        }
        catch (CatalogException ex)
        {
            return $"{ex.GetType().Name} after {ex.Attempts} attempts: {ex.Message}";
        }
    }
}
