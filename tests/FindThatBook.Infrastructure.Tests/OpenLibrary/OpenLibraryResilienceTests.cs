using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using H = FindThatBook.Infrastructure.Tests.OpenLibrary.OpenLibraryHarness;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>Retry table, Retry-After, throttle and circuit breaker (SY-01..05). Timeouts, slow empties and bursts have their own classes.</summary>
public sealed class OpenLibraryResilienceTests
{
    private static readonly CatalogQuery Dune = new() { Q = "dune" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retry_503_then_200_makes_two_requests()
    {
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(503), Reply.Hits("OL1W"));

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        response.Works.ShouldHaveSingleItem().WorkId.ShouldBe("OL1W");
        response.Stats.Attempts.ShouldBe(2);
        ol.RequestCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(400, typeof(CatalogBadResponseException))]
    [InlineData(404, typeof(CatalogBadResponseException))]
    [InlineData(401, typeof(CatalogBadResponseException))]
    [InlineData(422, typeof(CatalogQueryRejectedException))] // IN-005/IN-050: the pipeline re-plans, never a 502
    [InlineData(414, typeof(CatalogQueryRejectedException))]
    public async Task Non_transient_status_is_one_request(int status, Type expected)
    {
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(status), Reply.Hits("OL1W"));

        var ex = await Should.ThrowAsync<CatalogException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.ShouldBeOfType(expected);
        ex.Attempts.ShouldBe(1);
        ol.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task IN_050_live_422_body_gives_the_reason_without_echoing_the_query()
    {
        // Recorded live 2026-10-24: search.json?q=ab
        const string body = """
            {"detail":[{"type":"value_error","loc":["query","q"],"msg":"Value error, Query too short, must be at least 3 characters","input":"ab","ctx":{"error":{}}}]}
            """;
        await using var ol = H.Create();
        ol.Always(new Reply(422, body));

        var ex = await Should.ThrowAsync<CatalogQueryRejectedException>(() => ol.Catalog.SearchAsync(new CatalogQuery { Q = "ab" }, Ct));

        ex.Message.ShouldBe("Open Library rejected the query (HTTP 422): Value error, Query too short, must be at least 3 characters.");
        ex.Attempts.ShouldBe(1);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(429)]
    [InlineData(408)]
    public async Task Transient_status_every_time_is_1_plus_MaxRetries_then_unavailable(int status)
    {
        await using var ol = H.Create();
        ol.Always(Reply.Error(status));

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(3);
        ex.RetryAfter.ShouldBeNull();
        ol.RequestCount.ShouldBe(1 + ol.Options.MaxRetries);
    }

    [Fact]
    public async Task Retry_After_seconds_is_honoured()
    {
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(429).WithRetryAfter("1"), Reply.Hits("OL1W"));
        var clock = Stopwatch.StartNew();

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
        response.Stats.Attempts.ShouldBe(2);
        ol.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task SY_04_Retry_After_as_an_HTTP_date_is_honoured()
    {
        await using var ol = H.Create();
        var date = DateTimeOffset.UtcNow.AddSeconds(2).ToString("R", CultureInfo.InvariantCulture);
        ol.InSequence(Reply.Error(503).WithRetryAfter(date), Reply.Hits("OL1W"));

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        response.Stats.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task SY_03_long_Retry_After_fails_fast_as_unavailable_with_that_Retry_After()
    {
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(503).WithRetryAfter("120"), Reply.Hits("OL1W"));
        var clock = Stopwatch.StartNew();

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5)); // not the 10 s budget, let alone 120 s
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(120));
        ex.Attempts.ShouldBe(1);
        ol.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Connection_refused_is_retried_then_unavailable()
    {
        var closed = WireMock.Server.WireMockServer.Start();
        var url = closed.Url + "/";
        closed.Stop();
        closed.Dispose();
        await using var ol = H.Create(baseUrl: url);

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(3);
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Theory]
    [InlineData("<html>Service temporarily down</html>")]
    [InlineData("{\"numFound\": 3, \"docs\": [")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Unparseable_200_is_a_bad_response_and_not_retried(string body)
    {
        await using var ol = H.Create();
        ol.Always(Reply.Raw(body));

        var ex = await Should.ThrowAsync<CatalogBadResponseException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(1);
        ol.RequestCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Body_that_breaks_off_after_a_200_is_unavailable_not_a_crash(bool chunked)
    {
        // 200 headers, part of the JSON, then the connection closes: the failure surfaces while reading the body,
        // after the resilience handler has returned. (WireMock's own faults still send a complete, garbage 200.)
        await using var server = BrokenBodyServer.Start(chunked);
        await using var ol = H.Create(baseUrl: server.Url);

        var first = await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));
        await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct)); // not cached

        first.InnerException.ShouldBeAssignableTo<IOException>();
        first.Attempts.ShouldBe(1);
        first.RetryAfter.ShouldBeNull();
        server.Connections.ShouldBe(2);
        ol.Cache.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Redirects_are_not_followed()
    {
        // A followed redirect would be a second request that is neither throttled nor counted.
        await using var ol = H.Create();
        ol.Server.Given(H.Search()).AtPriority(1).RespondWith(WireMock.ResponseBuilders.Response.Create()
            .WithStatusCode(302).WithHeader("Location", "/elsewhere.json"));
        ol.Always(Reply.Hits("OL1W"), WireMock.RequestBuilders.Request.Create().WithPath("/elsewhere.json"));

        var ex = await Should.ThrowAsync<CatalogBadResponseException>(() => ol.Catalog.SearchAsync(Dune, Ct));

        ex.Attempts.ShouldBe(1);
        ol.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Throttle_sits_inside_the_retry_loop()
    {
        // RequestsPerSecond 1: the first attempt takes the only token, each retry must wait about a second for the next.
        var limiter = new CountingLimiter(OpenLibraryRateLimiter.Create(new OpenLibraryOptions { RequestsPerSecond = 1 }).Limiter);
        await using var ol = H.Create(new() { ["AttemptTimeout"] = "00:00:03", ["TotalTimeout"] = "00:00:08" }, limiter);
        ol.InSequence(Reply.Error(503), Reply.Error(503), Reply.Hits("OL1W"));
        var clock = Stopwatch.StartNew();

        var response = await ol.Catalog.SearchAsync(Dune, Ct);

        response.Stats.Attempts.ShouldBe(3);
        limiter.Acquisitions.ShouldBe(3);
        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(0.9)); // attempts 2 and 3 are a full refill apart
    }

    [Fact]
    public async Task SY_01_burst_overflow_is_not_retried_and_does_not_open_the_breaker()
    {
        // 40 parallel calls through a 3 req/s bucket with no queue: about 3 go out, the rest overflow. Overflow must
        // fail fast without being retried or counted, or the breaker would open while Open Library is healthy.
        var limiter = new CountingLimiter(OpenLibraryRateLimiter.Create(new OpenLibraryOptions { RequestsPerSecond = 3, QueueLimit = 0 }).Limiter);
        await using var ol = H.Create(new() { ["SearchCacheDuration"] = "00:00:00" }, limiter);
        ol.Always(Reply.Hits("OL1W"));
        var catalog = ol.Catalog;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 40).Select(async i =>
            await Record.ExceptionAsync(() => catalog.SearchAsync(new CatalogQuery { Q = $"burst {i}" }, Ct))));

        var failures = outcomes.OfType<Exception>().ToList();
        failures.Count.ShouldBeGreaterThanOrEqualTo(20); // a refill tick during a slow burst lets a few more through
        failures.ShouldAllBe(e => e is CatalogUnavailableException && e.InnerException is RateLimiterRejectedException);
        failures.Cast<CatalogUnavailableException>().ShouldAllBe(e => e.RetryAfter == TimeSpan.FromSeconds(1) && e.Attempts == 0);
        ol.RequestCount.ShouldBe(40 - failures.Count);
        limiter.Acquisitions.ShouldBe(40); // one token request per call: nothing was retried

        await Task.Delay(TimeSpan.FromSeconds(1.1), Ct); // tokens refill; the breaker must still be closed
        (await catalog.SearchAsync(Dune, Ct)).Works.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Queued_requests_wait_for_a_token_instead_of_failing()
    {
        await using var ol = H.Create(new()
        {
            ["RequestsPerSecond"] = "2", ["QueueLimit"] = "10", ["SearchCacheDuration"] = "00:00:00",
        });
        ol.Always(Reply.Hits("OL1W"));
        var catalog = ol.Catalog;
        var clock = Stopwatch.StartNew();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => catalog.SearchAsync(new CatalogQuery { Q = $"queued {i}" }, Ct)));

        responses.ShouldAllBe(r => r.Works.Count == 1);
        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(700)); // the last two waited for the refill
    }

    [Fact]
    public async Task Circuit_breaker_opens_after_repeated_failures_and_then_sends_nothing()
    {
        await using var ol = H.Create(new() { ["BreakerDuration"] = "00:00:20" });
        ol.Always(Reply.Error(503));
        var catalog = ol.Catalog;

        // MinimumThroughput 10: three failing searches are 9 failed attempts, the fourth opens the circuit.
        for (var i = 0; i < 4; i++)
            await Should.ThrowAsync<CatalogUnavailableException>(() => catalog.SearchAsync(new CatalogQuery { Q = $"fail {i}" }, Ct));
        var sent = ol.RequestCount;

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => catalog.SearchAsync(Dune, Ct));

        sent.ShouldBe(10);
        ex.InnerException.ShouldBeAssignableTo<BrokenCircuitException>();
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(20));
        ex.Attempts.ShouldBe(0);
        ol.RequestCount.ShouldBe(sent);
    }

    [Fact]
    public async Task One_failed_search_does_not_open_the_breaker()
    {
        await using var ol = H.Create();
        ol.InSequence(Reply.Error(503), Reply.Error(503), Reply.Error(503), Reply.Hits("OL1W"));

        await Should.ThrowAsync<CatalogUnavailableException>(() => ol.Catalog.SearchAsync(Dune, Ct));
        var next = await ol.Catalog.SearchAsync(new CatalogQuery { Q = "next" }, Ct);

        next.Works.ShouldHaveSingleItem();
    }

    [Fact]
    public void Retry_After_parsing()
    {
        var time = TimeProvider.System;
        using var none = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        using var seconds = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        seconds.Headers.TryAddWithoutValidation("Retry-After", "7");
        using var past = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        past.Headers.TryAddWithoutValidation("Retry-After", "Wed, 21 Oct 2015 07:28:00 GMT");

        OpenLibraryResilience.RetryAfter(none, time).ShouldBeNull();
        OpenLibraryResilience.RetryAfter(seconds, time).ShouldBe(TimeSpan.FromSeconds(7));
        OpenLibraryResilience.RetryAfter(past, time).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Token_bucket_matches_the_options()
    {
        using var limiter = OpenLibraryRateLimiter.Create(new OpenLibraryOptions { RequestsPerSecond = 3, QueueLimit = 0 });

        Enumerable.Range(0, 3).ShouldAllBe(_ => limiter.Limiter.AttemptAcquire(1).IsAcquired);
        limiter.Limiter.AttemptAcquire(1).IsAcquired.ShouldBeFalse();
        limiter.Limiter.ShouldBeOfType<TokenBucketRateLimiter>();
    }
}
