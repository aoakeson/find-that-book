using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using H = FindThatBook.Infrastructure.Tests.OpenLibrary.OpenLibraryHarness;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>IN-064/DA-064: a slow 200 with 0 hits is re-asked once, lighter and unretried, and never cached.</summary>
public sealed class OpenLibrarySlowEmptyTests
{
    private static readonly CatalogQuery Dune = new() { Q = "dune" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IN_064_slow_empty_is_requeried_once_lighter_and_not_cached()
    {
        await using var ol = H.Create();
        ol.Always(Reply.NoHits.After(2500));
        ol.Always(Reply.Hits("OL1W", "OL2W"), H.Search().WithParam("limit", "10"), priority: 1);
        var query = new CatalogQuery { Q = "king" };

        var response = await ol.Catalog.SearchAsync(query, Ct);

        response.Works.Select(w => w.WorkId).ShouldBe(["OL1W", "OL2W"]);
        response.Stats.SlowEmptyRetried.ShouldBeTrue();
        response.Stats.Attempts.ShouldBe(2);
        var requests = ol.Requests;
        requests.Count.ShouldBe(2);
        requests[1]["q"].ShouldBe("king");
        requests[1]["fields"].ShouldBe(OpenLibraryUrls.BaseFields);
        requests[1]["limit"].ShouldBe("10");

        var again = await ol.Catalog.SearchAsync(query, Ct);
        again.Stats.CacheHit.ShouldBeFalse();
        ol.RequestCount.ShouldBe(4);
        ol.Cache.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task DA_064_still_empty_after_the_requery_is_a_timeout_never_a_none()
    {
        await using var ol = H.Create();
        ol.Always(Reply.NoHits.After(2500));
        ol.Always(Reply.NoHits, H.Search().WithParam("limit", "10"), priority: 1);
        var query = new CatalogQuery { Q = "the bible" };

        var ex = await Should.ThrowAsync<CatalogTimeoutException>(() => ol.Catalog.SearchAsync(query, Ct));

        ex.Attempts.ShouldBe(2);
        await Should.ThrowAsync<CatalogTimeoutException>(() => ol.Catalog.SearchAsync(query, Ct)); // not cached
        ol.RequestCount.ShouldBe(4);
        ol.Cache.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Slow_empty_requery_is_never_retried()
    {
        await using var ol = H.Create();
        ol.Always(Reply.NoHits.After(2500));
        ol.Always(Reply.Error(503), H.Search().WithParam("limit", "10"), priority: 1);

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(new CatalogQuery { Q = "bible" }, Ct));

        ex.Attempts.ShouldBe(2);
        ol.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task Slow_answer_with_hits_is_not_requeried()
    {
        await using var ol = H.Create();
        ol.Always(Reply.Hits("OL1W").After(2500));

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        response.Stats.SlowEmptyRetried.ShouldBeFalse();
        response.Stats.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Worst_case_request_count_per_logical_call_is_MaxRequestsPerCall()
    {
        // Two 503s, then a slow empty 200, then the re-query: 1 + MaxRetries + 1 = 4 requests.
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(503), Reply.Error(503), Reply.NoHits.After(2500), Reply.Error(503));

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(ol.Options.MaxRequestsPerCall);
        ol.RequestCount.ShouldBe(4);
    }
}
