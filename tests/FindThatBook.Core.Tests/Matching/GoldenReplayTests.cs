using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// The production configuration (C# cleaner, every added rule on) replayed over the recorded golden (v1, 60) and hard
/// (46) pools, interpreter on (recorded hypotheses) and off. Golden is scored by its own rules
/// (status in the accepted set, top-1 in the accepted ids or by an accepted author); hard by top-1 correctness
/// (the expected work, an accepted duplicate, or the expected title by the expected author). Known misses are listed with their reason; a known miss that starts passing fails the
/// test so the list stays honest.
/// </summary>
public sealed class GoldenReplayTests
{
    /// <summary>Golden cases the build misses on the recorded pools (all also missed by the reference ranker).</summary>
    private static readonly Dictionary<string, string> GoldenKnownMisses = new()
    {
        ["golden:brief-01:off"] = "LLM off: 'mark' is a given name, so Huckleberry Finn is tier C (near title + author) → ambiguous; LLM on matches",
        ["golden:brief-06:off"] = "austen bennet: a surname + a character name; no title evidence → ambiguous (interpreter-dependent; the expectation stays)",
        ["golden:brief-06:on"] = "austen bennet: the recorded hypothesis gives no title → ambiguous (interpreter-dependent; the expectation stays)",
        ["golden:character-02:off"] = "ahab whale LLM off: Moby-Dick #1 via person: but tier E → ambiguous; LLM on matches",
        ["golden:character-04:off"] = "holden caulfield LLM off: Bloom's 'Holden Caulfield' is a typed exact title (tier A) → wrong #1 and match; LLM on is right",
    };

    private static readonly Dictionary<string, string> HardKnownMisses = new()
    {
        ["hard:holden caulfield:off"] = "same as golden character-04",
        ["hard:jean valjean:off"] = "LLM off: a book titled 'Jean Valjean' is a typed exact title (tier A)",
        ["hard:lotr:off"] = "LLM off: an acronym (IN-059, LLM-only by design)",
    };

    public static TheoryData<string> Golden => Filter("golden:");
    public static TheoryData<string> Hard => Filter("hard:");

    [Theory]
    [MemberData(nameof(Golden))]
    public void Golden_case_meets_its_expectation(string id)
    {
        var c = PoolFixtures.Case(id);
        var replay = Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
        var e = c.Expected!;
        var ok = e.Status.Contains(replay.Ranking.Status) && (TopOk(replay, e) ?? true);
        if (GoldenKnownMisses.TryGetValue(id, out var why))
            ok.ShouldBeFalse($"known miss now passes, remove it from the list: {why}");
        else
            ok.ShouldBeTrue($"status {replay.Ranking.Status}, top {string.Join(",", replay.TopIds(3))}, steps {string.Join(",", replay.Steps)}");
    }

    [Theory]
    [MemberData(nameof(Hard))]
    public void Hard_case_has_the_right_top_work(string id)
    {
        var c = PoolFixtures.Case(id);
        var replay = Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
        var ok = replay.Ranking.Ranked.Count > 0 && HardCorrect(replay.Top.Work, c.Expected!);
        if (HardKnownMisses.TryGetValue(id, out var why))
            ok.ShouldBeFalse($"known miss now passes, remove it from the list: {why}");
        else
            ok.ShouldBeTrue($"top {string.Join(",", replay.TopIds(3))}, status {replay.Ranking.Status}");
    }

    /// <summary>Every golden `match` the build makes is on an accepted top-1 (match precision 1.0 apart from the known holden miss).</summary>
    [Fact]
    public void Golden_matches_are_precise()
    {
        var wrong = new List<string>();
        foreach (var id in PoolFixtures.Load(PoolFixtures.GoldenHard).Keys.Where(k => k.StartsWith("golden:", StringComparison.Ordinal)))
        {
            var c = PoolFixtures.Case(id);
            var replay = Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
            if (replay.Ranking.Status == MatchStatus.Match && TopOk(replay, c.Expected!) == false) wrong.Add(id);
        }
        wrong.ShouldBe(["golden:character-04:off"]);
    }

    public static TheoryData<string> AllBookQueries => PoolFixtures.Ids(PoolFixtures.GoldenHard, PoolFixtures.Targeted);

    /// <summary>
    /// Topic requests never change a book search: on every golden, hard and targeted pool the topic rules are inert (no
    /// topic request is detected, and the full output with them on equals the output with them off).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllBookQueries))]
    public void Topic_rules_never_fire_on_book_queries(string id)
    {
        var c = PoolFixtures.Case(id);
        var query = QueryCleaner.Clean(c.Query);
        TopicRequest.From(query, c.Hypothesis).ShouldBeNull();
        var on = Replay.Run(c, query, RankerOptions.Default);
        var off = Replay.Run(c, query, RankerOptions.Default with { Topics = false });
        Signature(on).ShouldBe(Signature(off));
        on.Ranking.TopicLabel.ShouldBeNull();
    }

    private static string Signature(ReplayResult r)
        => $"{r.Ranking.Status}/{r.Ranking.Reason}/{string.Join(",", r.Issued.Select(i => i.Query.ToString()))}|"
           + string.Join("|", r.Ranking.Ranked.Select(w => $"{w.Work.WorkId}:{w.Tier}:{w.Score:R}:{string.Join(",", w.FoldedWorkIds)}:"
                                                           + $"{w.Evidence.TitleSource}:{string.Join(",", w.Evidence.MatchedSubjects)}"));

    private static bool? TopOk(ReplayResult r, Expectation e)
    {
        if (r.Ranking.Ranked.Count == 0) return e.Top1.Count == 0 && e.AuthorKeys.Count == 0 ? null : false;
        if (e.AuthorKeys.Count > 0) return r.Top.Work.AuthorKeys.Any(e.AuthorKeys.Contains);
        if (e.Top1.Count > 0) return e.Top1.Contains(r.Top.Work.WorkId);
        return null;
    }

    private static bool HardCorrect(CandidateWork w, Expectation e)
    {
        if (e.Work is { } id && (w.WorkId == id || e.Accept.Contains(w.WorkId))) return true;
        var author = e.Author is null ? null : PersonNames.Key(e.Author);
        var authors = w.AuthorNames.Select(PersonNames.Key).ToList();
        if (e.Kind == "author_only" || e.Work is null) return author is not null && authors.Contains(author);
        string Strip(string t) => string.Join(' ', TitleText.Key(t).Split(' ').Where(x => !Vocabulary.Noise.Contains(x)));
        return author is not null && Strip(w.Title) == Strip(e.Title ?? "") && authors.Contains(author);
    }

    private static TheoryData<string> Filter(string prefix)
    {
        var data = new TheoryData<string>();
        foreach (var id in PoolFixtures.Load(PoolFixtures.GoldenHard).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            data.Add(id);
        return data;
    }
}
