using System.Net;
using FindThatBook.Api.Tests.Support;

namespace FindThatBook.Api.Tests.Http;

/// <summary>
/// DE-05, IN-001..IN-020 and SY-11..SY-19. The pipeline is faked, so these test only what the
/// HTTP layer accepts, rejects and passes on. Errors are keyed by C# member name: "Q", "Limit".
/// </summary>
public sealed class ValidationTests(FakeSearchApi fixture) : IClassFixture<FakeSearchApi>
{
    private readonly FakeBookSearch _search = fixture.Reset().Search;
    private readonly HttpClient _client = fixture.Client;

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "IN-001 empty", "q=" },
        { "missing q", "limit=5" },
        { "IN-002 whitespace only", "q=%20%20%20" },
        { "IN-003 punctuation only", "q=%E2%80%94" },
        { "IN-004 zero-width only", "q=%E2%80%8B%E2%80%8B" },
        { "IN-015 control characters only", "q=%00%01%1F" },
        { "golden none-04 symbols only", "q=%3F!%3F%20...%20%23%23%23" },
        { "IN-013 301 characters", "q=" + new string('a', 301) },
        { "IN-013 300 + an emoji is 302 UTF-16 units", "q=" + new string('a', 299) + "%F0%9F%93%9A" },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task InvalidQuery_Is400_ValidationFailed_NamingQ(string caseId, string query)
    {
        var problem = await (await _client.GetAsync($"/api/books/search?{query}", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");

        problem.Errors("Q").ShouldNotBeEmpty(caseId);
        problem.ContainsKey("hint").ShouldBeFalse();
        _search.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DE-05 single stop word", "the")]
    [InlineData("IN-007 only stop words", "a of the")]
    public async Task OnlyStopWords_Is400_NoSearchableTerms_WithHint(string caseId, string q)
    {
        var problem = await (await _client.GetAsync($"/api/books/search?q={Uri.EscapeDataString(q)}", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "no_searchable_terms");

        problem["hint"]!.GetValue<string>().ShouldBe("Add a title or author.", caseId);
        problem.Errors("Q").ShouldNotBeEmpty();
        _search.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SY13_RepeatedQ_Is400_InsteadOfSilentlyJoining()
    {
        var problem = await (await _client.GetAsync("/api/books/search?q=a&q=b", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");

        problem.Errors("Q").ShouldBe(["Give q only once."]);
        _search.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("SY-11 not a number", "abc")]
    [InlineData("SY-11 exponent", "1e9")]
    [InlineData("SY-11 empty", "")]
    [InlineData("SY-11 decimal", "5.0")]
    [InlineData("SY-11 overflows int", "99999999999")]
    [InlineData("SY-12 zero", "0")]
    [InlineData("SY-12 above max", "11")]
    [InlineData("SY-12 negative", "-1")]
    [InlineData("leading plus", "+5")]
    public async Task BadLimit_Is400_NamingLimit(string caseId, string limit)
    {
        var problem = await (await _client.GetAsync($"/api/books/search?q=hobbit&limit={Uri.EscapeDataString(limit)}", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");

        problem.Errors("Limit").ShouldBe(["The field Limit must be a whole number between 1 and 10."], caseId);
        problem.Errors("Q").ShouldBeEmpty();
        _search.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RepeatedLimit_Is400()
    {
        var problem = await (await _client.GetAsync("/api/books/search?q=hobbit&limit=3&limit=4", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Limit").ShouldBe(["Give limit only once."]);
    }

    [Fact]
    public async Task EveryProblemIsReported_AndAnyFormatErrorOutranksNoSearchableTerms()
    {
        var problem = await (await _client.GetAsync("/api/books/search?q=the&limit=abc", TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Q").ShouldNotBeEmpty();
        problem.Errors("Limit").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SY19_NonJsonAccept_StillGetsTheSameProblemJson()
    {
        var request = Wire.Get("/api/books/search?q=", headers: ("Accept", "application/xml"));
        var problem = await (await _client.SendAsync(request, TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Q").ShouldNotBeEmpty();
    }

    public static TheoryData<string, string, string, int> ValidQueries => new()
    {
        // case, query string, what the pipeline receives, limit it receives
        { "default limit is 5", "q=hobbit", "hobbit", 5 },
        { "SY-12 limit with spaces", "q=hobbit&limit=%205", "hobbit", 5 },
        { "limit 1", "q=hobbit&limit=1", "hobbit", 1 },
        { "limit 10", "q=hobbit&limit=10", "hobbit", 10 },
        { "IN-012 exactly 300 characters", "q=" + new string('a', 300), new string('a', 300), 5 },
        { "DE-05 CJK under 3 characters", "q=%E4%B8%89%E4%BD%93", "三体", 5 },
        { "DE-05 two letters", "q=it", "it", 5 },
        { "DE-05 letter and dot", "q=v.", "v.", 5 },
        { "IN-002 trimmed", "q=%20%20the%20hobbit%20%20", "the hobbit", 5 },
        { "IN-015 NUL removed", "q=harry%20potter%00", "harry potter", 5 },
        { "IN-019 zero-width inside a word removed", "q=hob%E2%80%8Bbit", "hobbit", 5 },
        { "IN-017 NBSP kept for the cleaner", "q=the%C2%A0hobbit", "the hobbit", 5 },
        { "SY-14 markup is just text", "q=%3Cscript%3Ehobbit%3C/script%3E", "<script>hobbit</script>", 5 },
        { "SY-18 parameter names are case-insensitive", "Q=hobbit", "hobbit", 5 },
        { "SY-18 unknown parameters are ignored", "q=hobbit&utm_source=x", "hobbit", 5 },
    };

    [Theory]
    [MemberData(nameof(ValidQueries))]
    public async Task ValidQuery_ReachesThePipeline_Normalized(string caseId, string query, string expectedQuery, int expectedLimit)
    {
        var response = await _client.GetAsync($"/api/books/search?{query}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, caseId);
        _search.Calls.ShouldHaveSingleItem().ShouldBe((expectedQuery, expectedLimit), caseId);
    }
}
