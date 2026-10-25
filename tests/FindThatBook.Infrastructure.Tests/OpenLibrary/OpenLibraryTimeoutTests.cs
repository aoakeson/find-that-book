using System.Diagnostics;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using H = FindThatBook.Infrastructure.Tests.OpenLibrary.OpenLibraryHarness;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>Attempt and call timeouts, the Retry-After budget and caller cancellation. A class of its own so its slow scripted replies run in parallel with the rest.</summary>
public sealed class OpenLibraryTimeoutTests
{
    private static readonly CatalogQuery Dune = new() { Q = "dune" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SY_03_Retry_After_that_would_overrun_the_call_budget_stops_retrying()
    {
        // Each 2 s wait fits MaxRetryAfter, but the second would end past the 3 s call budget, so the call stops
        // with 503 + Retry-After instead of sleeping into a timeout. (Usually after 2 attempts; after 1 if the
        // first answer alone took over a second on a loaded machine. Either way the budget rule stopped it.)
        await using var ol = H.Create(new()
        {
            ["AttemptTimeout"] = "00:00:02", ["TotalTimeout"] = "00:00:03", ["MaxRetryAfter"] = "00:00:02",
            ["SlowEmptyThreshold"] = "00:00:00.500", ["QueueLimit"] = "0",
        });
        ol.Always(Reply.Error(429).WithRetryAfter("2"));

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBeInRange(1, 2);
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Attempt_timeout_sends_one_lighter_requery()
    {
        // The first reply hangs 5x past the 1 s attempt timeout; every later one is instant.
        await using var ol = H.Create(new() { ["AttemptTimeout"] = "00:00:01", ["SlowEmptyThreshold"] = "00:00:00.500" });
        ol.InSequence(Reply.Hits("OL9W").After(5000), Reply.Hits("OL1W"));

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        response.Works.ShouldHaveSingleItem().WorkId.ShouldBe("OL1W");
        response.Stats.Attempts.ShouldBe(2);
        response.Stats.SlowEmptyRetried.ShouldBeTrue();
        ol.Requests.ShouldContain(request => request["limit"] == "10" && request["fields"] == OpenLibraryUrls.BaseFields);
    }

    [Fact]
    public async Task Every_attempt_timing_out_is_a_timeout()
    {
        await using var ol = H.Create(new()
        {
            ["AttemptTimeout"] = "00:00:00.200", ["TotalTimeout"] = "00:00:06", ["MaxRetryAfter"] = "00:00:01",
            ["SlowEmptyThreshold"] = "00:00:00.100",
        });
        ol.Always(Reply.Hits("OL1W").After(4000));

        var ex = await Should.ThrowAsync<CatalogTimeoutException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Total_timeout_bounds_the_logical_call()
    {
        await using var ol = H.Create(new()
        {
            ["AttemptTimeout"] = "00:00:00.400", ["TotalTimeout"] = "00:00:00.600", ["MaxRetryAfter"] = "00:00:00.500",
            ["SlowEmptyThreshold"] = "00:00:00.100", ["QueueLimit"] = "0",
        });
        ol.Always(Reply.Hits("OL1W").After(5000));
        var clock = Stopwatch.StartNew();

        await Should.ThrowAsync<CatalogTimeoutException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3)); // gave up at the 0.6 s budget, not at the 5 s reply
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Caller_cancellation_stays_a_cancellation_and_reports_the_requests_sent(bool cache)
    {
        // A 503, then a reply that hangs: the caller's deadline fires during the second attempt.
        await using var ol = H.Create(new() { ["SearchCacheDuration"] = cache ? "01:00:00" : "00:00:00" });
        ol.InSequence(Reply.Error(503), Reply.Hits("OL1W").After(5000));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var ex = await Record.ExceptionAsync(() => ol.Catalog.SearchAsync(Dune, cts.Token));

        var cancelled = ex.ShouldBeOfType<CatalogCancelledException>(); // an OperationCanceledException, not a CatalogException
        cancelled.Attempts.ShouldBe(2);
        cancelled.CancellationToken.ShouldBe(cts.Token);
        ol.Cache.Writes.ShouldBeEmpty();
    }
}
