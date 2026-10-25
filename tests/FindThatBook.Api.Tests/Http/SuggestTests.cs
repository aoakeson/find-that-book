using System.Net;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Api.Tests.Http;

/// <summary>GET /api/suggest: typing states are 200s, bounds are 400s naming the field.</summary>
public sealed class SuggestTests(FakeSearchApi fixture) : IClassFixture<FakeSearchApi>
{
    private readonly FakeSearchApi _api = fixture.Reset();

    private Task<HttpResponseMessage> Get(string query) => _api.Client.GetAsync("/api/suggest" + query, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("no prefix", "")]
    [InlineData("empty prefix", "?prefix=")]
    [InlineData("whitespace", "?prefix=%20%20")]
    [InlineData("zero-width only", "?prefix=%E2%80%8B")]
    public async Task EmptyPrefix_IsAnEmptyList_NotAnError(string caseId, string query)
    {
        var response = await Get(query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, caseId);
        var body = await response.Read<SuggestResponse>();
        body.Prefix.ShouldBe("");
        body.Suggestions.ShouldBeEmpty();
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=3600");
    }

    [Fact]
    public async Task Prefix_MapsIndexHits_WithLinksAndCovers()
    {
        var response = await Get("?prefix=%20Har%20&limit=3");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.ShouldBeTrue();
        var body = await response.Read<SuggestResponse>();
        body.Prefix.ShouldBe("Har");
        _api.Api.Suggest.LastLookup.ShouldBe(("Har", 3));
        body.Suggestions.Count.ShouldBe(3);
        var first = body.Suggestions[0];
        first.ShouldBe(new SuggestionItem
        {
            WorkId = "OL82563W", Title = "Harry Potter and the Philosopher's Stone", Author = "J. K. Rowling", FirstPublishYear = 1997,
            CoverUrl = "https://covers.openlibrary.org/b/id/10521536-M.jpg", EditionCount = 337,
            OpenLibraryUrl = "https://openlibrary.org/works/OL82563W", Match = SuggestMatch.Title,
        });
        body.Suggestions[2].CoverUrl.ShouldBeNull();
        body.Suggestions[2].FirstPublishYear.ShouldBeNull(); // 0 in the index → unknown
        (await response.Json())["suggestions"]![2]!["match"]!.GetValue<string>().ShouldBe("word");
    }

    [Fact]
    public async Task DefaultLimit_Is8()
    {
        await Get("?prefix=ha");
        _api.Api.Suggest.LastLookup.ShouldBe(("ha", 8));
    }

    [Fact]
    public async Task NonLatinPrefix_IsPassedThrough()
    {
        (await Get("?prefix=%E4%B8%89")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Api.Suggest.LastLookup!.Value.Prefix.ShouldBe("三");
    }

    [Fact]
    public async Task HundredCharacters_IsAllowed_101_IsNot()
    {
        (await Get("?prefix=" + new string('a', 100))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var problem = await (await Get("?prefix=" + new string('a', 101))).ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Prefix").ShouldBe(["The field Prefix must be at most 100 characters."]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task BadLimit_Is400_NamingLimit(string limit)
    {
        var problem = await (await Get($"?prefix=har&limit={limit}")).ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Limit").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task RepeatedPrefix_Is400()
    {
        var problem = await (await Get("?prefix=a&prefix=b")).ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Prefix").ShouldBe(["Give prefix only once."]);
    }
}
