using System.Net;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Api.Tests.Http;

/// <summary>
/// What escapes the pipeline → the ProblemDetails catalog. Only Open Library failures with nothing in
/// hand get here; the LLM never does (that is a 200 with degraded, see PipelineTests).
/// </summary>
public sealed class ErrorMappingTests(FakeSearchApi fixture) : IClassFixture<FakeSearchApi>
{
    private readonly FakeSearchApi _api = fixture.Reset();

    private async Task<HttpResponseMessage> SearchThrowing(Exception ex, params (string, string)[] headers)
    {
        _api.Search.Handler = (_, _, _) => Task.FromException<SearchOutcome>(ex);
        return await _api.Client.SendAsync(Wire.Get("/api/books/search?q=tolkien%20hobbit", headers: headers), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Open Library said 7 s", 7.0, "7")]
    [InlineData("SY-03 a long Retry-After is passed on, not capped", 120.0, "120")]
    [InlineData("fractions round up", 1.2, "2")]
    public async Task Unavailable_Is503_WithUpstreamRetryAfter(string caseId, double seconds, string header)
    {
        var response = await SearchThrowing(new CatalogUnavailableException("503 after retries", 3, TimeSpan.FromSeconds(seconds)));

        var problem = await response.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "catalog_unavailable");
        response.Headers.RetryAfter!.ToString().ShouldBe(header, caseId);
        problem["detail"]!.GetValue<string>().ShouldContain("3 attempt");
    }

    [Fact]
    public async Task Unavailable_WithoutRetryAfter_Says15Seconds()
    {
        var response = await SearchThrowing(new CatalogUnavailableException("circuit open", 0));
        await response.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "catalog_unavailable");
        response.Headers.RetryAfter!.ToString().ShouldBe("15");
    }

    [Fact]
    public async Task CatalogTimeout_Is504_CatalogTimeout()
    {
        var response = await SearchThrowing(new CatalogTimeoutException("all attempts timed out", 3));
        await response.ShouldBeProblem(HttpStatusCode.GatewayTimeout, "catalog_timeout");
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Deadline_Is504_SearchTimeout()
    {
        var response = await SearchThrowing(new SearchTimeoutException(TimeSpan.FromSeconds(25)));
        await response.ShouldBeProblem(HttpStatusCode.GatewayTimeout, "search_timeout");
    }

    [Fact]
    public async Task BadResponse_Is502()
    {
        var response = await SearchThrowing(new CatalogBadResponseException("unparseable JSON", 1));
        await response.ShouldBeProblem(HttpStatusCode.BadGateway, "catalog_bad_response");
    }

    [Fact]
    public async Task SY31_Unhandled_Is500_WithoutLeakingDetails_InProduction()
    {
        var response = await SearchThrowing(new InvalidOperationException("boom: key=sk-supersecret123456 q=tolkien"));

        var problem = await response.ShouldBeProblem(HttpStatusCode.InternalServerError, "internal_error");
        var body = problem.ToJsonString();
        body.ShouldNotContain("supersecret");
        body.ShouldNotContain("InvalidOperationException");
        problem.ContainsKey("stack_trace").ShouldBeFalse();
        _api.Api.Logs.Named("request.unhandled").ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task SY19_ErrorsAreProblemJson_EvenForAnXmlClient()
    {
        var response = await SearchThrowing(new CatalogUnavailableException("down", 3), ("Accept", "application/xml"));
        await response.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "catalog_unavailable");
    }

    [Fact]
    public async Task UpstreamFailure_IsLoggedWithoutTheExceptionMessage()
    {
        await SearchThrowing(new CatalogUnavailableException("GET search.json?q=zebra+quokka failed", 3));

        var entry = _api.Api.Logs.Named("search.failed").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.AllText.ShouldNotContain("quokka");
        entry.AllText.ShouldContain("CatalogUnavailableException");
    }

    [Fact]
    public async Task SY29_ClientDisconnect_IsNotAnError()
    {
        var started = new TaskCompletionSource();
        var stopped = new TaskCompletionSource();
        _api.Search.Handler = async (_, _, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                stopped.SetResult();
            }
            throw new InvalidOperationException("unreachable");
        };
        using var cts = new CancellationTokenSource();
        var call = _api.Client.GetAsync("/api/books/search?q=hobbit", cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken); // let the middleware finish

        _api.Api.Logs.Entries.Where(e => e.Level >= LogLevel.Error).ShouldBeEmpty();
        _api.Api.Logs.Named("search.completed").ShouldBeEmpty();
    }
}

public sealed class ErrorDetailTests
{
    [Fact]
    public async Task Unhandled_InDevelopment_ShowsTheExceptionToTheDeveloper()
    {
        await using var api = new ApiFactory
        {
            EnvironmentName = "Development",
            Search = FakeBookSearch.Throwing(new InvalidOperationException("boom")),
        };
        var problem = await (await api.Client().GetAsync("/api/books/search?q=hobbit", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.InternalServerError, "internal_error");

        problem["exception"]!.GetValue<string>().ShouldBe("System.InvalidOperationException");
        problem["message"]!.GetValue<string>().ShouldBe("boom");
    }

    [Fact]
    public async Task APipelineThatIgnoresCancellation_IsCutOffAtTheDeadline_AndFreesItsPermit()
    {
        var hung = new TaskCompletionSource<SearchOutcome>(); // never completes and never looks at its token
        var search = new FakeBookSearch { Handler = (q, _, _) => q == "stuck" ? hung.Task : Task.FromResult(Outcomes.None(q)) };
        await using var api = new ApiFactory
        {
            Settings =
            {
                ["Search:RequestDeadline"] = "00:00:00.200", ["Search:PostRetrievalReserve"] = "00:00:00.100",
                ["RateLimit:SearchConcurrency"] = "1", ["RateLimit:SearchQueue"] = "0",
            },
            Search = search,
        };
        var client = api.Client();

        var started = DateTime.UtcNow;
        await (await client.GetAsync("/api/books/search?q=stuck", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.GatewayTimeout, "search_timeout");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));

        // The only concurrency permit came back with the 504, so the next search runs instead of a 429.
        (await client.GetAsync("/api/books/search?q=hobbit", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        hung.Task.IsCompleted.ShouldBeFalse();
    }
}
