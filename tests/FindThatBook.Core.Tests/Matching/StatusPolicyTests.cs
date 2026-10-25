using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>StatusPolicy decision table (AI-39..AI-53).</summary>
public sealed class StatusPolicyTests
{
    private const double T = 0.9;

    public static TheoryData<string, MatchStatus, StatusReason, MatchTier, bool, JudgeStatus, string?, double?, MatchStatus, StatusReason, string?> Table => new()
    {
        // case, code status, code reason, top tier, single token, verdict, pick, p → status, reason, second opinion
        { "judge off keeps the code match", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Off, null, null, MatchStatus.Match, StatusReason.ExactTitle, null },
        { "skipped keeps the code status", MatchStatus.Ambiguous, StatusReason.CloseScores, MatchTier.A, false, JudgeStatus.Skipped, null, null, MatchStatus.Ambiguous, StatusReason.CloseScores, null },
        { "unavailable keeps the code status", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Unavailable, null, null, MatchStatus.Match, StatusReason.ExactTitle, null },
        { "refused keeps the code status", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Refused, null, null, MatchStatus.Match, StatusReason.ExactTitle, null },
        { "agreed confidently confirms a match", MatchStatus.Match, StatusReason.ExactTitlePrimaryAuthor, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.95, MatchStatus.Match, StatusReason.JudgeAgreed, null },
        { "agreed on a tier B match confirms it", MatchStatus.Match, StatusReason.ExactTitleContributorAuthor, MatchTier.B, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Match, StatusReason.JudgeAgreed, null },
        { "AI-51 agreed below threshold is low confidence, not disagreement", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.8, MatchStatus.Ambiguous, StatusReason.JudgeLowConfidence, null },
        { "agreement lifts close scores", MatchStatus.Ambiguous, StatusReason.CloseScores, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.95, MatchStatus.Match, StatusReason.JudgeAgreed, null },
        { "AI-43 a one-word query is never lifted", MatchStatus.Ambiguous, StatusReason.CloseScores, MatchTier.A, true, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.CloseScores, null },
        { "AI-43 a one-word code match can still be confirmed", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, true, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Match, StatusReason.JudgeAgreed, null },
        { "a score conflict is guarded", MatchStatus.Ambiguous, StatusReason.ScoreConflict, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.ScoreConflict, null },
        { "an unsupported interpreted title is guarded", MatchStatus.Ambiguous, StatusReason.UnsupportedGuess, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.UnsupportedGuess, null },
        { "weak evidence is guarded", MatchStatus.Ambiguous, StatusReason.WeakEvidence, MatchTier.A, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.WeakEvidence, null },
        { "AI-52 tier C never becomes a match", MatchStatus.Ambiguous, StatusReason.NearTitleAuthor, MatchTier.C, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.NearTitleAuthor, null },
        { "tier D is never upgraded", MatchStatus.Ambiguous, StatusReason.AuthorOnly, MatchTier.D, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.Ambiguous, StatusReason.AuthorOnly, null },
        { "disagreement lowers a match and names the pick", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Disagreed, "W3", 0.97, MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, "W3" },
        { "a pick that differs from #1 is a disagreement whatever the adapter says", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Agreed, "W2", 0.97, MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, "W2" },
        { "tier C top can get a second opinion", MatchStatus.Ambiguous, StatusReason.NearTitleAuthor, MatchTier.C, false, JudgeStatus.Disagreed, "W2", 0.9, MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, "W2" },
        { "NONE is ambiguous", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.ChoseNone, null, 0.7, MatchStatus.Ambiguous, StatusReason.JudgeChoseNone, null },
        { "AI-47 an unknown pick is no verdict", MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A, false, JudgeStatus.Agreed, "OL0W", 0.99, MatchStatus.Match, StatusReason.ExactTitle, null },
        { "AI-40 no probability is no verdict", MatchStatus.Ambiguous, StatusReason.CloseScores, MatchTier.A, false, JudgeStatus.Agreed, "W1", null, MatchStatus.Ambiguous, StatusReason.CloseScores, null },
        { "code none stays none", MatchStatus.None, StatusReason.WeakEvidence, MatchTier.E, false, JudgeStatus.Agreed, "W1", 0.99, MatchStatus.None, StatusReason.WeakEvidence, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Decide(string _, MatchStatus codeStatus, StatusReason codeReason, MatchTier tier, bool singleToken,
        JudgeStatus judge, string? pick, double? p, MatchStatus status, StatusReason reason, string? secondOpinion)
    {
        var ranking = Ranking(codeStatus, codeReason, tier, singleToken);
        var d = StatusPolicy.Decide(ranking, new JudgeVerdict(judge, pick, p), T);
        d.Status.ShouldBe(status);
        d.Reason.ShouldBe(reason);
        d.SecondOpinionWorkId.ShouldBe(secondOpinion);
    }

    [Fact]
    public void AI_43_hermione_never_becomes_a_match_even_when_the_judge_is_sure()
    {
        foreach (var id in new[] { "hard:hermione:off", "hard:hermione:on", "04-58:hermione:v2" })
        {
            var c = PoolFixtures.Case(id);
            var ranking = Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default).Ranking;
            foreach (var pick in ranking.Ranked.Take(3).Select(w => w.Work.WorkId).Append("OL52818W"))
                StatusPolicy.Decide(ranking, new JudgeVerdict(JudgeStatus.Agreed, pick, 0.99), T).Status.ShouldNotBe(MatchStatus.Match, $"{id} {pick}");
        }
    }

    [Fact]
    public void Hints_follow_the_status()
    {
        StatusPolicy.Decide(RankingResult.Empty, JudgeVerdict.Off, T).Hint.ShouldBe("Check the spelling or add an author.");
        StatusPolicy.Decide(Ranking(MatchStatus.Match, StatusReason.ExactTitle, MatchTier.A), JudgeVerdict.Off, T).Hint.ShouldBeNull();
        StatusPolicy.Decide(Ranking(MatchStatus.Ambiguous, StatusReason.CloseScores, MatchTier.A), JudgeVerdict.Off, T).Hint
            .ShouldBe(StatusPolicy.AmbiguousHint);
        var author = Ranking(MatchStatus.Ambiguous, StatusReason.AuthorOnly, MatchTier.D) with { Ladder = new LadderSignals { AuthorOnly = true, AuthorName = "Charles Dickens" } };
        StatusPolicy.Decide(author, JudgeVerdict.Off, T).Hint.ShouldBe("These are Charles Dickens's most-published works. Add a title to narrow it.");
    }

    [Theory]
    [InlineData(1, MatchTier.A, false, MatchStatus.Match, false)]   // the API rejects < 2 choices
    [InlineData(2, MatchTier.A, false, MatchStatus.Match, true)]
    [InlineData(2, MatchTier.C, false, MatchStatus.Ambiguous, true)] // C tops get the second-opinion slot
    [InlineData(5, MatchTier.D, true, MatchStatus.Ambiguous, false)]
    [InlineData(5, MatchTier.E, false, MatchStatus.Ambiguous, false)]
    [InlineData(5, MatchTier.E, false, MatchStatus.None, false)]
    public void ShouldJudge(int count, MatchTier tier, bool authorOnly, MatchStatus status, bool expected)
    {
        var r = Ranking(status, StatusReason.ExactTitle, tier, count: count) with { Ladder = new LadderSignals { AuthorOnly = authorOnly } };
        StatusPolicy.ShouldJudge(r).ShouldBe(expected);
    }

    [Fact]
    public void The_judge_request_is_deduped_capped_in_code_order_with_roles_and_editions() // AI-38, AI-39, AI-46
    {
        var works = new List<RankedWork>
        {
            Ranked("W1", MatchTier.B, primary: ["J.R.R. Tolkien"], contributors: [new("Charles Dixon", "OL1A", "adapter"), new("Sean Deming", "OL2A", "adapter")],
                title: "The Hobbit", subtitle: "Graphic Novel", year: 1990, editions: 10),
            Ranked("W1", MatchTier.A),
            Ranked(" ", MatchTier.A),
            Ranked("W2", MatchTier.A, title: new string('x', 300), year: 0, editions: 482),
            Ranked("W3", MatchTier.E),
        };
        var request = StatusPolicy.BuildJudgeRequest("hobbit dixon", new RankingResult(works, MatchStatus.Match, StatusReason.ExactTitle), 2);
        request.Query.ShouldBe("hobbit dixon");
        request.Candidates.Select(c => c.WorkId).ShouldBe(["W1", "W2"]);
        var gn = request.Candidates[0];
        gn.Title.ShouldBe("The Hobbit: Graphic Novel");
        gn.Authors.ShouldBe(["J.R.R. Tolkien", "Charles Dixon", "Sean Deming"]);
        gn.Contributors.ShouldBe(["Charles Dixon (adapter)", "Sean Deming (adapter)"]);
        gn.EditionCount.ShouldBe(10);
        gn.FirstPublishYear.ShouldBe(1990);
        request.Candidates[1].Title.Length.ShouldBe(200);
        request.Candidates[1].FirstPublishYear.ShouldBeNull();
    }

    [Fact]
    public void A_disagreeing_pick_below_the_fold_takes_the_last_slot()
    {
        var ranking = Ranking(MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, MatchTier.A, count: 8);
        var d = new StatusDecision(MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, null, "W7");
        StatusPolicy.Shown(ranking, d, 5).Select(w => w.Work.WorkId).ShouldBe(["W1", "W2", "W3", "W4", "W7"]);
        StatusPolicy.Shown(ranking, d with { SecondOpinionWorkId = "W3" }, 5).Select(w => w.Work.WorkId).ShouldBe(["W1", "W2", "W3", "W4", "W5"]);
        StatusPolicy.Shown(ranking, d, 2).Select(w => w.Work.WorkId).ShouldBe(["W1", "W7"]);
        StatusPolicy.Shown(ranking, d, 1).Select(w => w.Work.WorkId).ShouldBe(["W1"]); // AI-53: never replaces #1
        StatusPolicy.Shown(ranking, d with { SecondOpinionWorkId = null }, 3).Select(w => w.Work.WorkId).ShouldBe(["W1", "W2", "W3"]);
    }

    private static RankingResult Ranking(MatchStatus status, StatusReason reason, MatchTier tier, bool singleToken = false, int count = 3)
        => new([.. Enumerable.Range(1, count).Select(i => Ranked($"W{i}", i == 1 ? tier : MatchTier.E))], status, reason)
        {
            Facts = new StatusFacts { SingleTokenNoAuthor = singleToken },
        };

    private static RankedWork Ranked(string id, MatchTier tier, string[]? primary = null, AuthorCredit[]? contributors = null,
        string title = "Title", string? subtitle = null, int? year = null, int editions = 1)
        => new()
        {
            Work = new CandidateWork { WorkId = id, Title = title, Subtitle = subtitle, FirstPublishYear = year, EditionCount = editions },
            Tier = tier,
            Score = 1,
            PrimaryAuthors = [.. (primary ?? ["Author"]).Select(n => new AuthorCredit(n, null))],
            Contributors = contributors ?? [],
            Evidence = new MatchEvidence(),
        };
}
