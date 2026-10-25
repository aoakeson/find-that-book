using System.Net;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Api.Tests.Pipeline;

/// <summary>
/// The real SearchService through HTTP with every port faked (catalog, interpreter, explanation writer, judge).
/// These check the seams between the API and the pipeline, not ranking quality (that is Core's and the eval's).
/// </summary>
public sealed class PipelineTests
{
    private static readonly CandidateWork Hobbit = new()
    {
        WorkId = "OL27482W", Title = "The Hobbit", AuthorNames = ["J.R.R. Tolkien"], AuthorKeys = ["OL26320A"],
        FirstPublishYear = 1937, EditionCount = 482, CoverId = 14627509, ReadingLogCount = 9000,
    };

    private static readonly CandidateWork Fellowship = new()
    {
        WorkId = "OL27513W", Title = "The Fellowship of the Ring", AuthorNames = ["J.R.R. Tolkien"], AuthorKeys = ["OL26320A"],
        FirstPublishYear = 1954, EditionCount = 319,
    };

    private static ApiFactory Api(Func<CatalogQuery, CatalogResponse>? catalog = null)
    {
        var api = new ApiFactory();
        api.Catalog.Respond = catalog ?? (_ => new CatalogResponse([Hobbit, Fellowship], 2, new CatalogCallStats(false, 1, 40)));
        return api;
    }

    private static Task<HttpResponseMessage> Search(ApiFactory api, string q = "tolkien hobbit") =>
        api.Client().GetAsync($"/api/books/search?q={Uri.EscapeDataString(q)}", TestContext.Current.CancellationToken);

    [Fact]
    public async Task LlmDown_IsA200_Degraded_WithTheHeuristicAndTemplates()
    {
        await using var api = Api();
        api.Interpreter.Respond = _ => FakeInterpreter.Failed(LlmCallStatus.RateLimited);
        api.Explainer.Respond = _ => new(null, Outcomes.Llm("explain", null, null, 3, LlmCallStatus.ProviderError));

        var response = await Search(api);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var r = await response.Read<SearchResponse>();
        r.Degraded.ShouldBeTrue();
        r.DegradedReasons.ShouldContain(DegradedReason.InterpretationFallback);
        r.Interpretation.Source.ShouldBe(InterpretationSource.Heuristic);
        r.Candidates.ShouldNotBeEmpty();
        r.Candidates.ShouldAllBe(c => c.ExplanationSource == ExplanationSource.Template && c.Explanation.Length > 0);
        r.Meta.Llm.Steps.ShouldContain(s => s.Operation == "interpret" && s.Status == LlmCallStatus.RateLimited);
    }

    [Fact]
    public async Task Meta_CountsWhatTheFakesSaw()
    {
        await using var api = Api();
        api.Interpreter.Respond = _ => FakeInterpreter.Ok(new QueryHypothesis("The Hobbit", "J.R.R. Tolkien", [], [], null, null));

        var r = await (await Search(api)).Read<SearchResponse>();

        r.Meta.OpenLibrary.Calls.ShouldBe(api.Catalog.Calls);
        r.Meta.OpenLibrary.Calls.ShouldBeLessThanOrEqualTo(r.Meta.OpenLibrary.Budget);
        r.Meta.Llm.Steps.Count(s => s.Operation == "interpret").ShouldBe(api.Interpreter.Calls);
        r.Interpretation.Source.ShouldBe(InterpretationSource.Llm);
        r.Candidates.Count.ShouldBeLessThanOrEqualTo(5);
        r.Candidates[0].WorkId.ShouldBe("OL27482W");
        r.Meta.Judge.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Limit_IsPassedThrough()
    {
        await using var api = Api();
        var response = await api.Client().GetAsync("/api/books/search?q=tolkien%20hobbit&limit=1", TestContext.Current.CancellationToken);
        (await response.Read<SearchResponse>()).Candidates.Count.ShouldBe(1);
    }

    public static TheoryData<string, Exception, HttpStatusCode, string> Failures => new()
    {
        { "unavailable", new CatalogUnavailableException("503", 3, TimeSpan.FromSeconds(4)), HttpStatusCode.ServiceUnavailable, "catalog_unavailable" },
        { "timeout", new CatalogTimeoutException("timeout", 3), HttpStatusCode.GatewayTimeout, "catalog_timeout" },
        { "bad response", new CatalogBadResponseException("bad json", 1), HttpStatusCode.BadGateway, "catalog_bad_response" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task OpenLibraryDown_WithNothingInHand_MapsToTheCatalog(string caseId, Exception failure, HttpStatusCode status, string code)
    {
        await using var api = Api(_ => throw failure);
        var response = await Search(api);
        await response.ShouldBeProblem(status, code);
        if (failure is CatalogUnavailableException) response.Headers.RetryAfter!.ToString().ShouldBe("4", caseId);
    }
}
