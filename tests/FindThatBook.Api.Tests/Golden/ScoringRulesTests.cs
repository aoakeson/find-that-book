using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Api.Tests.Golden;

/// <summary>The eval's scoring block (eval/golden.v2.json "scoring"), as tests: if these move, every published number moves.</summary>
public sealed class ScoringRulesTests
{
    private static readonly Expectation Hobbit = new() { Statuses = ["match"], Top1 = ["OL27482W"], Top5 = ["OL27482W"] };

    private static Observed Shown(string outcome, params string[] ids) => new(outcome, null, ids.Select(id => ObservedWork.Of(id)).ToList());

    [Fact]
    public void Score_TitleRowRightAtRankOne_PassesTopOneTopFiveStatusAndMatch()
    {
        var s = CaseScorer.Score(Hobbit, Shown("match", "OL27482W", "OL219602W"));

        s.ShouldBe(new CaseScore(Answerable: true, Top1: true, Top5: true, Status: true, IsMatch: true, MatchCorrect: true, FalseMatch: false, TargetRank: 1));
    }

    [Fact]
    public void Score_TitleRowRightAtRankThree_PassesTopFiveOnlyAndWrongMatchCountsAgainstPrecision()
    {
        var s = CaseScorer.Score(Hobbit, Shown("match", "OL1W", "OL2W", "OL27482W"));

        s.Top1.ShouldBe(false);
        s.Top5.ShouldBe(true);
        s.TargetRank.ShouldBe(3);
        s.MatchCorrect.ShouldBe(false); // a "match" on the wrong book is the costly error
    }

    [Fact]
    public void Score_RightBookAtRankSix_IsNotTopFive()
    {
        var s = CaseScorer.Score(Hobbit, Shown("ambiguous", "OL1W", "OL2W", "OL3W", "OL4W", "OL5W", "OL27482W"));

        s.Top5.ShouldBe(false);
        s.TargetRank.ShouldBeNull();
    }

    [Theory] // golden contrib-01: either Open Library record of the Dixon graphic novel is right
    [InlineData("OL219602W")]
    [InlineData("OL19622663W")]
    public void Score_MultipleAcceptedIds_AnyOfThemPasses(string shown)
    {
        var e = new Expectation { Statuses = ["match", "ambiguous"], Top1 = ["OL219602W", "OL19622663W"], Top5 = ["OL219602W", "OL19622663W"] };

        CaseScorer.Score(e, Shown("ambiguous", shown)).Top1.ShouldBe(true);
    }

    [Fact] // golden contrib-02: Tolkien's original is acceptable in the top 5 but not at rank 1
    public void Score_IdAcceptedOnlyInTopFiveList_FailsTopOnePassesTopFive()
    {
        var e = new Expectation { Statuses = ["match", "ambiguous"], Top1 = ["OL219602W"], Top5 = ["OL219602W", "OL27482W"] };

        var s = CaseScorer.Score(e, Shown("ambiguous", "OL27482W"));

        s.Top1.ShouldBe(false);
        s.Top5.ShouldBe(true);
    }

    public sealed record AuthorCase(string Name, Expectation Expected, ObservedWork Work, bool Hit)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<AuthorCase> AuthorCases => new()
    {
        new AuthorCase("author-03 twain: duplicate author record key counts",
            new Expectation { Statuses = ["ambiguous"], AuthorKeys = ["OL18319A", "OL9937375A"] },
            ObservedWork.Of("OL1W", "Roughing It", ["Mark Twain"], ["OL9937375A"]), true),
        new AuthorCase("author-03 twain: same name, unlisted key does not count",
            new Expectation { Statuses = ["ambiguous"], AuthorKeys = ["OL18319A"] },
            ObservedWork.Of("OL1W", "x", ["Mark Twain"], ["OL999A"]), false),
        new AuthorCase("ambig-02 meyer: any Meyer by surname",
            new Expectation { Statuses = ["ambiguous"], AuthorSurname = "meyer" },
            ObservedWork.Of("OL1W", "Battlefield of the Mind", ["Joyce Meyer"]), true),
        new AuthorCase("ambig-02 meyer: Meyers is a different surname",
            new Expectation { Statuses = ["ambiguous"], AuthorSurname = "meyer" },
            ObservedWork.Of("OL1W", "x", ["Stephen Meyers"]), false),
        new AuthorCase("held-out author: NameKey matches an inverted MARC name",
            new Expectation { Statuses = ["ambiguous"], AuthorNames = ["Ursula K. Le Guin"] },
            ObservedWork.Of("OL1W", "x", ["Le Guin, Ursula K."]), true),
        new AuthorCase("held-out author: initials with and without dots",
            new Expectation { Statuses = ["ambiguous"], AuthorNames = ["J. K. Rowling"] },
            ObservedWork.Of("OL1W", "x", ["J.K. Rowling"]), true),
        new AuthorCase("held-out author: diacritics fold",
            new Expectation { Statuses = ["ambiguous"], AuthorNames = ["Gabriel Garcia Marquez"] },
            ObservedWork.Of("OL1W", "x", ["Gabriel García Márquez"]), true),
    };

    [Theory, MemberData(nameof(AuthorCases))]
    public void Hit_AuthorRows_FollowGoldenAuthorRules(AuthorCase c)
    {
        c.Expected.IsAuthorRow.ShouldBeTrue();
        CaseScorer.Hit(c.Expected, c.Work, top1: true).ShouldBe(c.Hit);
    }

    [Fact] // brief-04 dickens: author-only rows never allow "match", even with the right author first
    public void Score_AuthorRowReturnedAsMatch_FailsStatusAndPrecision()
    {
        var e = new Expectation { Statuses = ["ambiguous"], AuthorKeys = ["OL24638A"] };
        var o = new Observed("match", null, [ObservedWork.Of("OL1W", "A Christmas Carol", ["Charles Dickens"], ["OL24638A"])]);

        var s = CaseScorer.Score(e, o);

        s.Top1.ShouldBe(true);
        s.Status.ShouldBe(false);
        s.MatchCorrect.ShouldBe(false);
    }

    [Theory] // none-01 / none-03: only status is scored; any match is a false positive
    [InlineData("none", true, false)]
    [InlineData("ambiguous", true, false)]
    [InlineData("match", false, true)]
    public void Score_NoAnswerRow_ScoresStatusOnly(string outcome, bool statusOk, bool falseMatch)
    {
        var e = new Expectation { Statuses = ["none", "ambiguous"] };

        var s = CaseScorer.Score(e, Shown(outcome, "OL1W"));

        s.Answerable.ShouldBeFalse();
        s.Top1.ShouldBeNull();
        s.Status.ShouldBe(statusOk);
        s.FalseMatch.ShouldBe(falseMatch);
    }

    [Theory] // golden v2 none-04 ("?!? ... ###"): DE-05 / requirement R2 (docs/TRACEABILITY.md) → 400 validation_failed
    [InlineData("rejected", "validation_failed", true, false)]
    [InlineData("rejected", "no_searchable_terms", false, false)]
    [InlineData("none", null, false, false)]
    [InlineData("match", null, false, true)]
    public void Score_RejectedRow_PassesOnlyWhenValidatorRejectsWithTheExpectedCode(string outcome, string? code, bool statusOk, bool falseMatch)
    {
        var e = new Expectation { Statuses = ["rejected"], ErrorCode = "validation_failed", HttpStatus = 400 };

        var s = CaseScorer.Score(e, new Observed(outcome, code, []));

        e.IsRejection.ShouldBeTrue();
        s.Answerable.ShouldBeFalse();
        s.Status.ShouldBe(statusOk);
        s.FalseMatch.ShouldBe(falseMatch);
    }

    [Fact]
    public void Score_HeldOutRule_WrongMatchIsAlsoAStatusError()
    {
        var golden = Hobbit with { Statuses = ["match", "ambiguous"] };
        var heldOut = golden with { WrongMatchIsStatusError = true };
        var wrong = Shown("match", "OL1W", "OL27482W");

        CaseScorer.Score(golden, wrong).Status.ShouldBe(true);
        CaseScorer.Score(heldOut, wrong).Status.ShouldBe(false);
    }

    [Theory] // The research prototype's eval.py lenient rule for jsonl sets: a duplicate record of the same book counts
    [InlineData("A Separate Peace", "John Knowles", true)]
    [InlineData("A separate peace (Bantam classic)", "Knowles, John", true)]
    [InlineData("A Separate Peace: a novel", "John Knowles", true)]
    [InlineData("A Separate Peace", "Somebody Else", false)]
    [InlineData("Peace", "John Knowles", false)]
    public void Hit_LenientRow_SameMainTitleAndAuthorCounts(string title, string author, bool hit)
    {
        var e = new Expectation { Top1 = ["OL267913W"], Top5 = ["OL267913W"], LenientTitle = "A Separate Peace", LenientAuthor = "John Knowles" };

        CaseScorer.Hit(e, ObservedWork.Of("OL999W", title, [author]), top1: true).ShouldBe(hit);
    }

    [Fact]
    public void Compute_MixedRows_UsesGoldenDenominators()
    {
        var rows = new (string, CaseScore)[]
        {
            ("match", CaseScorer.Score(Hobbit, Shown("match", "OL27482W"))),                              // right match
            ("match", CaseScorer.Score(Hobbit, Shown("match", "OL1W"))),                                  // wrong match
            ("ambiguous", CaseScorer.Score(Hobbit, Shown("ambiguous", "OL27482W"))),                      // right, not auto-accepted
            ("match", CaseScorer.Score(new Expectation { Statuses = ["none"] }, Shown("match", "OL1W"))), // false match
            ("skipped", new CaseScore(true, null, null, null, false, null, false, null)),                 // not run: excluded
        };

        var q = QualityStats.Compute(rows);

        q.Rows.ShouldBe(4);
        q.Top1.ShouldBe(new Ratio(2, 3));
        q.MatchPrecision.ShouldBe(new Ratio(1, 3));
        q.Coverage.ShouldBe(new Ratio(2, 3));
        q.Status.ShouldBe(new Ratio(2, 4));
        q.FalseMatches.ShouldBe(1);
        q.MatchPrecision.ToString().ShouldBe("1/3 = .333");
        new Ratio(0, 0).ToString().ShouldBe("n/a");
    }
}
