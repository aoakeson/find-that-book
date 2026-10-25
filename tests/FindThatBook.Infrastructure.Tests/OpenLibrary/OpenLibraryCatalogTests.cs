using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>Recorded-fixture replay, URL encoding on the wire, caching and call stats.</summary>
public sealed class OpenLibraryCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Fixtures.Names), MemberType = typeof(Fixtures))]
    public async Task Recorded_fixture_replays_through_the_client(string name)
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.ServeFixtures();
        var fixture = Fixtures.Named(name);

        var response = await ol.Catalog.SearchAsync(fixture.Query, Ct);

        response.NumFound.ShouldBe(fixture.NumFound);
        response.Works.Count.ShouldBe(fixture.Docs);
        response.Stats.ShouldBe(response.Stats with { CacheHit = false, Attempts = 1, SlowEmptyRetried = false });
    }

    [Fact]
    public async Task Golden_fixtures_carry_the_evidence_ranking_needs()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.ServeFixtures();
        async Task<IReadOnlyList<FindThatBook.Core.Model.CandidateWork>> Works(string fixture) =>
            (await ol.Catalog.SearchAsync(Fixtures.Named(fixture).Query, Ct)).Works;

        var hobbit = (await Works("tolkien-hobbit"))[0];
        hobbit.WorkId.ShouldBe("OL27482W");
        hobbit.FirstPublishYear.ShouldBe(1937);
        hobbit.EditionCount.ShouldBe(482);

        var graphic = (await Works("hobbit-dixon")).Single(w => w.WorkId == "OL219602W");
        graphic.AuthorNames.ShouldBe(["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"]); // Open Library order, adapters first: order is not authorship
        graphic.Contributors.ShouldContain("Charles Dixon (Adapter)");

        (await Works("dickens-author-key")).ShouldAllBe(w => w.AuthorKeys.Contains("OL24638A"));
        (await Works("pride-prejudice"))[0].WorkId.ShouldBe("OL66554W");
        (await Works("harry-potter-philosophers-stone"))[0].WorkId.ShouldBe("OL82563W");

        var deathsEnd = (await Works("three-body-problem"))[0]; // DA-035: English evidence for a Chinese-titled work
        deathsEnd.AuthorNames.ShouldBe(["刘慈欣"]);
        deathsEnd.AuthorAlternativeNames.ShouldContain("Liu Cixin");
        deathsEnd.EditionTitles.ShouldBe(["Death's End (The Three-Body Problem)"]);

        var santi = (await Works("santi-cjk-title")).ShouldHaveSingleItem(); // DE-05: title= for a 2-character query
        santi.WorkId.ShouldBe("OL17267881W");
        santi.Title.ShouldBe("三体");

        (await Works("person-harry-potter")).ShouldContain(w => w.Persons.Contains("Harry Potter"));

        var bears = await Works("subject-bears"); // topic call
        bears.Single(w => w.WorkId == "OL279316W").Subjects.ShouldContain("Bears");
        bears.ShouldAllBe(w => w.Subjects.Count > 0 && w.Subjects.Count <= OpenLibraryMapper.MaxSubjects);

        var suggested = await Works("suggestions-bear-titles"); // one OR-joined call verifies several suggestions
        suggested.Select(w => w.WorkId).ShouldContain("OL476641W");  // Winnie-the-Pooh
        suggested.Select(w => w.WorkId).ShouldContain("OL10490647W"); // A Bear Called Paddington
        suggested.Select(w => w.WorkId).ShouldContain("OL4114525W");  // Corduroy
        suggested.Single(w => w.WorkId == "OL476641W").Subjects.Count.ShouldBe(OpenLibraryMapper.MaxSubjects); // 45 recorded, capped
    }

    [Theory]
    [InlineData("person:(harry AND potter)", null)]
    [InlineData(null, "三体")]
    [InlineData(null, "채식주의자")]
    [InlineData("pride & prejudice", null)]
    [InlineData("subject:(paris (france))", null)]
    [InlineData("title:(winnie pooh) OR title:(paddington) OR title:(corduroy)", null)]
    public async Task Server_receives_the_exact_text(string? q, string? title)
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.NoHits);

        await ol.Catalog.SearchAsync(new CatalogQuery { Q = q, Title = title }, Ct);

        var received = ol.Requests.ShouldHaveSingleItem();
        received["q"].ShouldBe(q);
        received["title"].ShouldBe(title);
        received["limit"].ShouldBe("20");
        received["fields"].ShouldBe(OpenLibraryUrls.BaseFields + "," + OpenLibraryUrls.V3Fields);
    }

    [Fact]
    public async Task Sends_the_configured_user_agent()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.NoHits);

        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune" }, Ct);

        var headers = ol.Server.LogEntries.Single().RequestMessage!.Headers!;
        string.Join(' ', headers["User-Agent"]).ShouldBe(new OpenLibraryOptions().UserAgent);
    }

    [Fact]
    public async Task Second_identical_search_is_a_cache_hit_with_no_requests()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Hits("OL27482W", "OL219602W"));
        var query = new CatalogQuery { Q = "tolkien hobbit" };

        var first = await ol.Catalog.SearchAsync(query, Ct);
        var second = await ol.Catalog.SearchAsync(query with { }, Ct);

        first.Stats.CacheHit.ShouldBeFalse();
        first.Stats.Attempts.ShouldBe(1);
        second.Stats.CacheHit.ShouldBeTrue();
        second.Stats.Attempts.ShouldBe(0);
        second.Works.Select(w => w.WorkId).ShouldBe(["OL27482W", "OL219602W"]);
        second.NumFound.ShouldBe(2);
        ol.RequestCount.ShouldBe(1);
        ol.Cache.Writes.ShouldHaveSingleItem().Expiration.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Different_queries_are_cached_separately()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Hits("OL1W"));

        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune" }, Ct);
        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune", Limit = 10 }, Ct);
        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune", IncludePersons = true }, Ct);
        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune", IncludeSubjects = true }, Ct); // the fields list is part of the key

        ol.RequestCount.ShouldBe(4);
    }

    [Fact]
    public async Task SY_20_identical_concurrent_searches_share_one_request()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Hits("OL27482W").After(1500)); // long enough for all 20 to join
        var catalog = ol.Catalog;

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => catalog.SearchAsync(new CatalogQuery { Q = "hobbit" }, Ct)));

        ol.RequestCount.ShouldBe(1);
        responses.Count(r => !r.Stats.CacheHit).ShouldBe(1);
        responses.Where(r => r.Stats.CacheHit).ShouldAllBe(r => r.Stats.Attempts == 0 && r.Works.Count == 1);
    }

    [Fact]
    public async Task A_caller_that_joined_a_failing_fetch_reports_no_requests_of_its_own()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Error(500).After(1500));
        var catalog = ol.Catalog;

        var failures = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            (CatalogUnavailableException)(await Record.ExceptionAsync(() => catalog.SearchAsync(new CatalogQuery { Q = "hobbit" }, Ct)))!));

        ol.RequestCount.ShouldBe(3);
        failures.Count(f => f.Attempts == 3).ShouldBe(1);
        failures.Count(f => f.Attempts == 0).ShouldBe(9);
    }

    [Fact]
    public async Task SY_23_long_CJK_query_is_still_cached()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Hits("OL17267881W"));
        var query = new CatalogQuery { Title = new string('体', 300) };

        await ol.Catalog.SearchAsync(query, Ct);
        var again = await ol.Catalog.SearchAsync(query, Ct);

        again.Stats.CacheHit.ShouldBeTrue();
        ol.RequestCount.ShouldBe(1);
        ol.Cache.Writes.ShouldHaveSingleItem().Key.Length.ShouldBe(70);
    }

    [Fact]
    public async Task Fast_empty_result_is_cached_for_the_shorter_empty_lifetime()
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.NoHits);
        var query = new CatalogQuery { Q = "purple elephant who sang opera mars" };

        var first = await ol.Catalog.SearchAsync(query, Ct);
        var second = await ol.Catalog.SearchAsync(query, Ct);

        first.Works.ShouldBeEmpty();
        first.Stats.SlowEmptyRetried.ShouldBeFalse();
        second.Stats.CacheHit.ShouldBeTrue();
        ol.RequestCount.ShouldBe(1);
        ol.Cache.Writes.ShouldHaveSingleItem().Expiration.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Empty_results_are_not_cached_when_the_empty_lifetime_is_zero()
    {
        await using var ol = OpenLibraryHarness.Create(new() { ["EmptySearchCacheDuration"] = "00:00:00" });
        ol.Always(Reply.NoHits);

        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "nothing" }, Ct);
        var second = await ol.Catalog.SearchAsync(new CatalogQuery { Q = "nothing" }, Ct);

        second.Stats.CacheHit.ShouldBeFalse();
        ol.RequestCount.ShouldBe(2);
        ol.Cache.Writes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(500, 6)] // 1 + 2 retries, twice
    [InlineData(422, 2)]
    [InlineData(400, 2)]
    public async Task Failures_are_never_cached(int status, int expectedRequests)
    {
        await using var ol = OpenLibraryHarness.Create();
        ol.Always(Reply.Error(status));

        for (var i = 0; i < 2; i++)
            await Should.ThrowAsync<CatalogException>(() => ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune" }, Ct));

        ol.RequestCount.ShouldBe(expectedRequests);
        ol.Cache.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cache_can_be_switched_off()
    {
        await using var ol = OpenLibraryHarness.Create(new() { ["SearchCacheDuration"] = "00:00:00" });
        ol.Always(Reply.Hits("OL1W"));

        await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune" }, Ct);
        var second = await ol.Catalog.SearchAsync(new CatalogQuery { Q = "dune" }, Ct);

        second.Stats.CacheHit.ShouldBeFalse();
        second.Stats.Attempts.ShouldBe(1);
        ol.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task Query_without_terms_is_rejected_without_a_request()
    {
        await using var ol = OpenLibraryHarness.Create();

        var ex = await Should.ThrowAsync<CatalogQueryRejectedException>(() => ol.Catalog.SearchAsync(new CatalogQuery { Q = "  " }, Ct));

        ex.Attempts.ShouldBe(0);
        ol.RequestCount.ShouldBe(0);
    }
}
