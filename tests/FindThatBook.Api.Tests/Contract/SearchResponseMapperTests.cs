using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests.Contract;

/// <summary>The mapping rules that the approved-JSON tests don't isolate: caps, nulls and measured-only numbers.</summary>
public sealed class SearchResponseMapperTests
{
    private static readonly SearchResponseMapper Mapper = new(Options.Create(new OpenLibraryOptions()), new SearchOptions(), new FakePricing());

    private static SearchCandidate Candidate(RankedWork work) => Mapper.MapCandidate(Outcomes.Item(1, work, "x"));

    private static readonly AuthorCredit[] FiveAuthors =
        [new("A One", "OL1A"), new("B Two", "OL2A"), new("C Three", "OL3A"), new("D Four", "OL4A"), new("E Five", "OL5A")];

    [Fact]
    public void PrimaryAuthors_AreCappedAtThree_KeepingTheMatchedOne()
    {
        var c = Candidate(Outcomes.Work("OL1W", "Anthology", MatchTier.C, 1, FiveAuthors,
            evidence: new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "e five" }));

        c.PrimaryAuthors.Select(a => a.Name).ShouldBe(["A One", "B Two", "E Five"]);
        c.PrimaryAuthorsTotal.ShouldBe(5);
        c.Author.ShouldBe("A One, B Two, E Five and 2 more");
    }

    [Fact]
    public void Contributors_AreCappedAtThree_KeepingTheMatchedOne()
    {
        var contributors = FiveAuthors.Select(a => a with { Role = "illustrator" }).ToArray();
        var c = Candidate(Outcomes.Work("OL1W", "Picture Book", MatchTier.B, 1, [new("Writer", "OL9A")], contributors,
            evidence: new MatchEvidence { Role = AuthorRole.Contributor, MatchedAuthor = "D Four" }));

        c.Contributors.Select(a => a.Name).ShouldBe(["A One", "B Two", "D Four"]);
        c.ContributorsTotal.ShouldBe(5);
        c.Author.ShouldBe("Writer"); // the brief's author field never lists contributors
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(1937, 1937)]
    public void ImplausibleYears_AreNull(int year, int? expected) =>
        Candidate(Outcomes.Work("OL1W", "T", MatchTier.E, 1, [], year: year)).FirstPublishYear.ShouldBe(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(14627509, "https://covers.openlibrary.org/b/id/14627509-M.jpg")]
    public void CoverUrl_IsNullWithoutACover(int? cover, string? expected) =>
        Candidate(Outcomes.Work("OL1W", "T", MatchTier.E, 1, [], cover: cover)).CoverUrl.ShouldBe(expected);

    [Theory]
    [InlineData("OL27482W", "https://openlibrary.org/works/OL27482W")]
    [InlineData("OL17318741M", "https://openlibrary.org/books/OL17318741M")] // DA-059: an orphan edition is not a work
    public void OpenLibraryUrl_LinksWorksAndOrphanEditions(string id, string url) =>
        Candidate(Outcomes.Work(id, "T", MatchTier.E, 1, [])).OpenLibraryUrl.ShouldBe(url);

    [Fact]
    public void OpenLibraryUrl_OpensOnTheMatchedEdition()
    {
        var work = Outcomes.Work("OL27482W", "The Hobbit", MatchTier.A, 1, []);
        Candidate(work with { Work = work.Work with { EditionKey = "OL51711263M" } })
            .OpenLibraryUrl.ShouldBe("https://openlibrary.org/works/OL27482W?edition=key%3A/books/OL51711263M");
    }

    [Theory]
    [InlineData("OL13476400W", "OL25760876M", "https://openlibrary.org/works/OL13476400W?edition=key%3A/books/OL25760876M")]
    [InlineData("OL13476400W", null, "https://openlibrary.org/works/OL13476400W")]
    [InlineData("OL13476400W", "junk/../x", "https://openlibrary.org/works/OL13476400W")] // only a well-formed edition key is linked
    [InlineData("OL17318741M", "OL25760876M", "https://openlibrary.org/books/OL17318741M")]
    public void OpenLibraryUrl_OpensTheWorkOnTheMatchedEdition(string id, string? edition, string url)
    {
        var ranked = Outcomes.Work(id, "The Book of Mormon", MatchTier.A, 1, []);
        Candidate(ranked with { Work = ranked.Work with { EditionKey = edition } }).OpenLibraryUrl.ShouldBe(url);
    }

    [Fact]
    public void AWorkWithNoAuthors_HasANullAuthorField()
    {
        var c = Candidate(Outcomes.Work("OL1W", "Beowulf", MatchTier.A, 1, []));
        c.Author.ShouldBeNull();
        c.PrimaryAuthors.ShouldBeEmpty();
        c.MatchTierLabel.ShouldBe("Exact title (no author given)");
    }

    [Fact]
    public void AuthorWithoutKey_HasNoUrl() =>
        Candidate(Outcomes.Work("OL1W", "T", MatchTier.E, 1, [new("Anon", null)])).PrimaryAuthors.Single().Url.ShouldBeNull();

    private static LlmMeta Llm(params LlmCall[] calls) => Mapper.Map(Outcomes.Build(Outcomes.Query("q"), null, MatchStatus.None,
        StatusReason.NoCandidates, null, [], JudgeVerdict.Off, [], [], calls, [], 1), "id").Meta.Llm;

    [Fact]
    public void NoLlmCalls_CostsZero_AndNamesNoProvider()
    {
        var llm = Llm();
        llm.Calls.ShouldBe(0);
        llm.InputTokens.ShouldBe(0);
        llm.EstCostUsd.ShouldBe(0m);
        llm.Provider.ShouldBeNull();
    }

    [Fact]
    public void CacheHitsAndSkippedCalls_SpendNothing()
    {
        var llm = Llm(Outcomes.Llm("interpret", 400, 60, 1, cacheHit: true), Outcomes.Llm("explain", null, null, 0, LlmCallStatus.NotConfigured),
            Outcomes.Llm("explain", null, null, 0, LlmCallStatus.BudgetExhausted)); // a cooldown or budget skip never left the process
        llm.Calls.ShouldBe(0);
        llm.CacheHits.ShouldBe(1);
        llm.EstCostUsd.ShouldBe(0m);
        llm.PromptVersions.ShouldBe(new Dictionary<string, string> { ["interpret"] = "interpret.v3", ["explain"] = "explain.v3" });
    }

    [Fact]
    public void ARequestWithoutUsage_MakesTokensAndCostUnknown_NotZero()
    {
        var llm = Llm(Outcomes.Llm("interpret", 400, 60, 600), Outcomes.Llm("explain", null, null, 5000, LlmCallStatus.Timeout));
        llm.Calls.ShouldBe(2);
        llm.InputTokens.ShouldBeNull();
        llm.EstCostUsd.ShouldBeNull();
    }

    [Fact]
    public void AModelMissingFromThePriceTable_HasUnknownCost()
    {
        var llm = Llm(Outcomes.Llm("interpret", 400, 60, 600, model: "mystery-model"));
        llm.InputTokens.ShouldBe(400);
        llm.EstCostUsd.ShouldBeNull();
    }

    [Fact]
    public void Cost_IsTheSumOverProviderRequests()
    {
        var llm = Llm(Outcomes.Llm("interpret", 1_000_000, 0, 1), Outcomes.Llm("explain", 0, 1_000_000, 1));
        llm.EstCostUsd.ShouldBe(2.80m);
    }

    [Fact]
    public void SlowEmptyRequery_IsNotARetry()
    {
        CatalogCallRecord Call(int attempts, bool slowEmpty) => new("broad", "q=…", CatalogCallOutcome.Ok, false, attempts, 9000, 0, 0, slowEmpty, null);
        var ol = Mapper.Map(Outcomes.Build(Outcomes.Query("king"), null, MatchStatus.None, StatusReason.NoCandidates, null, [],
            JudgeVerdict.Off, [], [Call(3, slowEmpty: true), Call(2, slowEmpty: false), Call(1, slowEmpty: true)], [], [], 1), "id").Meta.OpenLibrary;

        ol.UpstreamRequests.ShouldBe(6);
        ol.Retries.ShouldBe(2);           // 1 (3 attempts − the re-query − the first) + 1 + 0
        ol.SlowEmptyRequeries.ShouldBe(2);
    }

    private static JudgeMeta Judge(JudgeVerdict verdict) => Mapper.Map(Outcomes.Build(Outcomes.Query("q"), null,
        MatchStatus.None, StatusReason.NoCandidates, null, [], verdict, [], [], [], [], 1), "id").Meta.Judge;

    [Fact]
    public void SY30_NaNProbability_NeverReachesJson() =>
        Judge(new JudgeVerdict(JudgeStatus.Disagreed, "OL1W", double.NaN, "gpt-6-luna", 400, 300)).Probability.ShouldBeNull();

    [Theory]
    [InlineData(JudgeStatus.Agreed, true)]
    [InlineData(JudgeStatus.Disagreed, false)]
    [InlineData(JudgeStatus.ChoseNone, false)]
    [InlineData(JudgeStatus.Unavailable, null)]
    [InlineData(JudgeStatus.Refused, null)]
    public void Agreed_IsNullWithoutAVerdict(JudgeStatus status, bool? agreed) =>
        Judge(new JudgeVerdict(status, "OL1W", 0.95, "gpt-6-luna", 400, 300)).Agreed.ShouldBe(agreed);

    [Fact]
    public void JudgeOff_IsDisabled_WithNoLatencyOrCost()
    {
        var j = Judge(JudgeVerdict.Off);
        j.Enabled.ShouldBeFalse();
        j.LatencyMs.ShouldBeNull();
        j.EstCostUsd.ShouldBeNull();
        j.Threshold.ShouldBe(0.9);
    }

    [Fact]
    public void JudgeSkipped_MeansSwitchedOnButNotNeeded()
    {
        var j = Judge(JudgeVerdict.Skipped("not_applicable"));
        j.Enabled.ShouldBeTrue();
        j.Detail.ShouldBe("not_applicable");
        j.LatencyMs.ShouldBeNull();
    }

    [Fact]
    public void JudgeCost_IsInputTokensOnly()
    {
        var j = Judge(new JudgeVerdict(JudgeStatus.Agreed, "OL1W", 0.99, "gpt-6-luna", 1_000_000, 250));
        j.EstCostUsd.ShouldBe(0.10m);
        j.LatencyMs.ShouldBe(250);
    }

    [Fact]
    public void HeuristicInterpretation_ShowsTheCleanedSearchWords()
    {
        var outcome = Outcomes.DegradedLlmDown() with { Query = Outcomes.Query("mark huckleberry 1876", ["mark", "huckleberry"], ["deluxe"], 1876) };
        var i = Mapper.Map(outcome, "id").Interpretation;
        i.Source.ShouldBe(InterpretationSource.Heuristic);
        i.Title.ShouldBeNull();
        i.Keywords.ShouldBe(["mark", "huckleberry"]);
        i.Year.ShouldBe(1876);
        i.RemovedNoise.ShouldBe(["deluxe"]);
    }
}
