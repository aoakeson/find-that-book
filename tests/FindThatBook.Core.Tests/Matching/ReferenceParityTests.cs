using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// The port is faithful: with every added rule off (<see cref="RankerOptions.Baseline"/>) and the reference ranker's own
/// cleaned query, the C# ranker reproduces the measured reference ranking, tiers, scores, folded duplicates and status
/// on every recorded pool (golden 60 + hard 46, interpreter on and off, plus the targeted QA and battle-test pools), and
/// the ladder makes the same catalog calls.
/// </summary>
public sealed class ReferenceParityTests
{
    public static TheoryData<string> Cases => PoolFixtures.Ids(PoolFixtures.GoldenHard, PoolFixtures.Targeted);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ranking_matches_the_reference(string id)
    {
        var c = PoolFixtures.Case(id);
        var result = MatchRanker.Rank(c.ReferenceCleanedQuery(), c.Hypothesis, c.Pool(), RankerOptions.Baseline);

        result.Ranked.Count.ShouldBe(c.Reference.Count);
        for (var i = 0; i < c.Reference.Ranked.Count; i++)
        {
            var (expected, actual) = (c.Reference.Ranked[i], result.Ranked[i]);
            var at = $"rank {i + 1}";
            actual.Work.WorkId.ShouldBe(expected.Id, at);
            actual.Tier.ShouldBe(expected.Tier, at);
            actual.Score.ShouldBe(expected.Score, 1e-6, at);
            actual.Evidence.ExactTitle.ShouldBe(expected.Exact, at);
            actual.Evidence.IntentMatch.ShouldBe(expected.IntentHit, at);
            actual.Evidence.Role.ShouldBe(expected.Role switch { "primary" => AuthorRole.Primary, "contributor" => AuthorRole.Contributor, _ => null }, at);
            actual.FoldedWorkIds.ShouldBe(expected.Dups, at);
        }
        result.Status.ShouldBe(c.Reference.Status);
        result.Ladder.AuthorOnly.ShouldBe(c.Reference.AuthorOnly);
        result.Ladder.Strong.ShouldBe(c.Reference.Strong);
        if (c.Reference.PickAuthor is { } author) result.Ladder.TopWorksAuthorKey.ShouldBe(author);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ladder_makes_the_references_calls(string id)
    {
        var c = PoolFixtures.Case(id);
        var replay = Replay.Run(c, c.ReferenceCleanedQuery(), RankerOptions.Baseline);

        replay.Unrecorded.ShouldBeEmpty();
        // SearchService runs the original-title call beside call 1 and the focused retry after it; the reference ran the retry first.
        Normalize(replay.Steps).ShouldBe(Normalize(c.Reference.Steps));
        replay.Ranking.Status.ShouldBe(c.Reference.Status);
        replay.Ranking.Ranked.Take(5).Select(w => w.Work.WorkId).ShouldBe(c.Reference.Ranked.Take(5).Select(r => r.Id));
    }

    private static List<string> Normalize(IReadOnlyList<string> steps)
    {
        // DE-05: the broad call for a < 3-character query is title= (short_title); the reference sent q= (and got a 422).
        var list = steps.Select(s => s == "short_title" ? "broad" : s).ToList();
        var (h, o) = (list.IndexOf("hypothesis"), list.IndexOf("original_title"));
        if (h > o && o >= 0) { list.RemoveAt(h); list.Insert(o, "hypothesis"); }
        return list;
    }
}
