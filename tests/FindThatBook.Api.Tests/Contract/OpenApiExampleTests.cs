using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.OpenApi;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Model;
using Microsoft.AspNetCore.Http;

namespace FindThatBook.Api.Tests.Contract;

/// <summary>
/// The named examples in Scalar can't drift from the contract: each one must deserialize into the real response
/// type with no missing, extra or misspelled field, re-serialize to the same JSON, and obey the status rules.
/// </summary>
public sealed class OpenApiExampleTests
{
    public static TheoryData<string> ExampleIds => [.. OpenApiExamples.All.Select(e => $"{e.OperationId}.{e.Status}.{e.Name}")];

    private static OpenApiExamples.Example Find(string id) => OpenApiExamples.All.Single(e => $"{e.OperationId}.{e.Status}.{e.Name}" == id);

    [Fact]
    public void TheDocumentedScenariosAllHaveExamples()
    {
        string[] required =
        [
            "SearchBooks.200.clear_match", "SearchBooks.200.contributor_match", "SearchBooks.200.ambiguous_author_only",
            "SearchBooks.200.none", "SearchBooks.200.degraded_llm_down", "SearchBooks.200.topic_search", "SearchBooks.200.topic_none", "SearchBooks.400.validation_failed",
            "SearchBooks.400.no_searchable_terms", "SearchBooks.429.rate_limited", "SearchBooks.503.catalog_unavailable",
            "SearchBooks.504.search_timeout", "SuggestBooks.200.prefix_harry", "Health.200.ok",
        ];
        var ids = OpenApiExamples.All.Select(e => $"{e.OperationId}.{e.Status}.{e.Name}").ToList();
        foreach (var id in required) ids.ShouldContain(id);
    }

    [Theory]
    [MemberData(nameof(ExampleIds))]
    public void Example_MatchesTheContractTypes(string id)
    {
        var example = Find(id);
        example.Summary.ShouldNotBeNullOrWhiteSpace();
        switch (example.OperationId, example.Status)
        {
            case ("SearchBooks", 200):
                CheckSearch(RoundTrip<SearchResponse>(example.Value));
                break;
            case ("SuggestBooks", 200):
                RoundTrip<SuggestResponse>(example.Value);
                break;
            case ("Health", 200):
                RoundTrip<HealthResponse>(example.Value).Status.ShouldBe("ok");
                break;
            default:
                CheckProblem(example);
                break;
        }
    }

    /// <summary>Strict read, then write back: equal JSON means every field of the type is present and nothing else is.</summary>
    private static T RoundTrip<T>(JsonNode value) where T : class
    {
        var typed = value.Deserialize<T>(Wire.StrictContract).ShouldNotBeNull();
        var back = JsonSerializer.SerializeToNode(typed, Wire.StrictContract);
        JsonNode.DeepEquals(value, back).ShouldBeTrue($"{typeof(T).Name} example differs after a round trip:\n{back}");
        return typed;
    }

    private static void CheckSearch(SearchResponse r)
    {
        r.Degraded.ShouldBe(r.DegradedReasons.Count > 0);
        if (r.Status == MatchStatus.None) r.Candidates.ShouldBeEmpty();
        else r.Candidates.ShouldNotBeEmpty();
        if (r.Status == MatchStatus.Match) r.Candidates[0].MatchTier.ShouldBeOneOf(MatchTier.A, MatchTier.B);
        if (r.Status != MatchStatus.Match) r.Hint.ShouldNotBeNullOrWhiteSpace();
        r.Candidates.Select(c => c.Rank).ShouldBe(Enumerable.Range(1, r.Candidates.Count));
        foreach (var c in r.Candidates)
        {
            c.OpenLibraryUrl.ShouldStartWith($"https://openlibrary.org/{(c.WorkId.EndsWith('M') ? "books" : "works")}/{c.WorkId}");
            c.PrimaryAuthors.Count.ShouldBeLessThanOrEqualTo(3);
            c.Contributors.Count.ShouldBeLessThanOrEqualTo(3);
            c.PrimaryAuthorsTotal.ShouldBeGreaterThanOrEqualTo(c.PrimaryAuthors.Count);
            c.MatchTierLabel.ShouldBe(SearchResponseMapper.TierLabel(c.MatchTier, new MatchEvidence { Role = c.MatchTier == MatchTier.A ? AuthorRole.Primary : null }));
            if (c.CoverUrl is not null) c.CoverUrl.ShouldMatch(@"^https://covers\.openlibrary\.org/b/id/\d+-M\.jpg$");
        }
        r.Meta.OpenLibrary.Calls.ShouldBe(r.Meta.OpenLibrary.Steps.Count);
        r.Meta.OpenLibrary.Calls.ShouldBeLessThanOrEqualTo(r.Meta.OpenLibrary.Budget);
        r.Meta.Llm.Steps.ShouldNotBeEmpty();
        r.Meta.Judge.Enabled.ShouldBe(r.Meta.Judge.Status != JudgeStatus.Off);
        if (r.Interpretation.Intent == QueryIntent.Book) r.Interpretation.Topics.ShouldBeEmpty();
        if (r.StatusReason == StatusReason.TopicSearch)
        {
            r.Interpretation.Intent.ShouldBe(QueryIntent.Topic);
            r.Interpretation.Topics.ShouldNotBeEmpty();
            r.Status.ShouldBe(MatchStatus.Ambiguous);
        }
        r.Candidates.ShouldAllBe(c => c.MatchedSubjects.Count <= 3);
    }

    private static void CheckProblem(OpenApiExamples.Example example)
    {
        var problem = example.Value.Deserialize<HttpValidationProblemDetails>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
        problem.Status.ShouldBe(example.Status);
        var code = problem.Extensions["code"].ShouldBeOfType<JsonElement>().GetString().ShouldNotBeNull();
        code.ShouldBe(example.Name);
        problem.Type.ShouldBe("/problems/" + code.Replace('_', '-'));
        problem.Title.ShouldNotBeNullOrWhiteSpace();
        problem.Extensions.ShouldContainKey("trace_id");
        if (example.Status == 400) problem.Errors.ShouldNotBeEmpty();
        else example.Value.AsObject().ContainsKey("errors").ShouldBeFalse();
    }
}
