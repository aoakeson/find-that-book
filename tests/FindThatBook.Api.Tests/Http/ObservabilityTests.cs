using System.Net;
using FindThatBook.Api.Tests.Support;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Api.Tests.Http;

/// <summary>One structured search.completed line per search, the query only at Debug, one id end to end.</summary>
public sealed class ObservabilityTests
{
    [Fact]
    public async Task EachSearch_LogsOneStructuredLine_WithTheResponseNumbers()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.ContributorMatch()) };
        var response = await api.Client().GetAsync("/api/books/search?q=hobbit%20dixon%20graphic%20novel", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = api.Logs.Named("search.completed").ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        var s = entry.State;
        s["RequestId"].ShouldBe(response.Headers.GetValues("X-Request-Id").Single());
        s["Status"].ShouldBe("match");
        s["TopTier"].ShouldBe("b");
        s["DegradedReasons"].ShouldBe("-");
        s["ElapsedMs"].ShouldBe(2530L);
        s["Candidates"].ShouldBe(2);
        s["LlmCalls"].ShouldBe(2);
        s["InputTokens"].ShouldBe(1225L);
        s["OutputTokens"].ShouldBe(231L);
        s["EstCostUsd"].ShouldBe(0.000945m);
        s["OlCalls"].ShouldBe(2);
        s["OlCacheHits"].ShouldBe(0);
        s["OlAttempts"].ShouldBe(3);
        s["JudgeStatus"].ShouldBe("off");
    }

    [Fact]
    public async Task SY33_TheQueryText_NeverReachesInformationLevel()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.None("zebra quokka marmalade")) };
        await api.Client().GetAsync("/api/books/search?q=zebra%20quokka%20marmalade", TestContext.Current.CancellationToken);

        api.Logs.Entries.Where(e => e.Level >= LogLevel.Information).ShouldNotContain(e => e.AllText.Contains("quokka"));
    }

    [Fact]
    public async Task DegradedSearch_LogsItsReasons()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.DegradedLlmDown()) };
        await api.Client().GetAsync("/api/books/search?q=mark%20huckleberry", TestContext.Current.CancellationToken);

        var s = api.Logs.Named("search.completed").ShouldHaveSingleItem().State;
        s["DegradedReasons"].ShouldBe("interpretation_fallback,explanation_fallback");
        s["InputTokens"].ShouldBe(0L); // the 429 is not billed; the budget-skipped explain never left the process
        s["LlmCalls"].ShouldBe(1);
    }
}
