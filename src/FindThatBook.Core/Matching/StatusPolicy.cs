using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching;

/// <summary>
/// Status with and without the judge, the second-opinion slot and hints (AI-39..AI-53). The judge never reorders. It
/// can confirm a code match, lower it, or lift the one soft code outcome (close scores within a tier, as measured);
/// every guarded outcome (an unsupported interpreted title, a score conflict, weak evidence, a one-word author-less
/// query, author-only, tier C/D/E) stays as code decided.
/// </summary>
public static class StatusPolicy
{
    public const string NoneHint = "Check the spelling or add an author.";
    public const string AmbiguousHint = "Several books fit. Add the author or more of the title to narrow it.";

    /// <summary>Topic hints. The page heads topic results (and an empty topic result) with the topic, so these only say how to refine.</summary>
    public const string NoTopicHint = "Add a topic, a title or an author.";
    public const string TopicHint = "Add a title or author to find a specific one.";
    public const string TopicNoneHint = "Try a broader topic, or add a title or author.";

    public static string AuthorOnlyHint(string author) => $"These are {author}'s most-published works. Add a title to narrow it.";

    /// <summary>Whether a judge call is meaningful: ≥ 2 candidates (the judge API rejects fewer choices), top tier A–C, not author-only.</summary>
    public static bool ShouldJudge(RankingResult ranking)
        => ranking.TopicLabel is null // a topic request has no one right book to confirm
           && ranking.Ranked.Count >= 2
           && ranking.Status != MatchStatus.None
           && ranking.Ranked[0].Tier is MatchTier.A or MatchTier.B or MatchTier.C
           && !ranking.Ladder.AuthorOnly;

    /// <summary>
    /// ≤ <paramref name="maxCandidates"/> deduped candidates in final code order (input order is what moved the judge's
    /// answers when measured); blank or repeated ids are skipped (AI-38/AI-47). Authors primary first, contributors with roles,
    /// edition counts and years so identical-looking twins differ (AI-39); lists and titles capped (AI-46).
    /// </summary>
    public static JudgeRequest BuildJudgeRequest(string query, RankingResult ranking, int maxCandidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<JudgeCandidate>();
        foreach (var w in ranking.Ranked)
        {
            if (list.Count >= maxCandidates) break;
            var id = w.Work.WorkId;
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            var title = string.IsNullOrWhiteSpace(w.Work.Subtitle) ? w.Work.Title : $"{w.Work.Title}: {w.Work.Subtitle}";
            var authors = w.PrimaryAuthors.Select(a => a.Name)
                .Concat(w.Contributors.Where(c => c.Key is not null || c.Role == AlsoCredited).Select(c => c.Name))
                .Distinct(StringComparer.Ordinal).Take(MaxNames).ToList();
            var contributors = w.Contributors.Where(c => c.Role is not null && c.Role != AlsoCredited)
                .Select(c => $"{c.Name} ({c.Role})").Distinct(StringComparer.Ordinal).Take(MaxNames).ToList();
            list.Add(new JudgeCandidate(id, Cap(title), authors, contributors,
                w.Work.FirstPublishYear is > 0 ? w.Work.FirstPublishYear : null, w.Work.EditionCount));
        }
        return new JudgeRequest(query, list);
    }

    /// <summary>
    /// Final status. No verdict (off, skipped, unavailable, refused, unusable pick) → the code status. NONE → ambiguous
    /// (judge_chose_none). A different pick → ambiguous with a second opinion. Agreement → match only when the top is A/B
    /// and code said match, or ambiguous only because of close scores (and the query is not one author-less word, AI-43),
    /// and p ≥ threshold; agreement below the threshold is ambiguous / judge_low_confidence, never "disagreed" (AI-51).
    /// </summary>
    public static StatusDecision Decide(RankingResult ranking, JudgeVerdict verdict, double threshold)
    {
        if (ranking.Ranked.Count == 0)
            return ranking.TopicLabel is not null ? CodeDecision(ranking) : new StatusDecision(MatchStatus.None, StatusReason.NoCandidates, NoneHint);
        var code = CodeDecision(ranking);
        if (verdict.Status is not (JudgeStatus.Agreed or JudgeStatus.Disagreed or JudgeStatus.ChoseNone)) return code;
        var top = ranking.Ranked[0];
        if (code.Status == MatchStatus.None || top.Tier is MatchTier.D or MatchTier.E) return code;

        if (verdict.Status == JudgeStatus.ChoseNone || verdict.Pick is null)
            return new StatusDecision(MatchStatus.Ambiguous, StatusReason.JudgeChoseNone, NoneHint);
        if (!ranking.Ranked.Any(w => w.Work.WorkId == verdict.Pick)) return code; // unknown choice = no verdict (AI-47)
        if (verdict.Pick != top.Work.WorkId)
            return new StatusDecision(MatchStatus.Ambiguous, StatusReason.JudgeDisagreed, AmbiguousHint, verdict.Pick);

        if (verdict.Probability is not { } p) return code; // only probabilities[choice] counts, never "confidence" (AI-40)
        var upgradable = ranking.Status == MatchStatus.Match
                         || (ranking.Reason == StatusReason.CloseScores && !ranking.Facts.SingleTokenNoAuthor);
        if (top.Tier is not (MatchTier.A or MatchTier.B) || !upgradable) return code; // AI-52, guarded outcomes, AI-43
        return p >= threshold
            ? new StatusDecision(MatchStatus.Match, StatusReason.JudgeAgreed, null)
            : new StatusDecision(MatchStatus.Ambiguous, StatusReason.JudgeLowConfidence, AmbiguousHint);
    }

    /// <summary>
    /// The works shown: the top <paramref name="limit"/> in code order. When the judge disagreed and its pick sits lower,
    /// the pick takes the last slot (N ≥ 2); with <paramref name="limit"/> = 1 it is not shown, so the judge never
    /// replaces code's #1 (AI-53).
    /// </summary>
    public static IReadOnlyList<RankedWork> Shown(RankingResult ranking, StatusDecision decision, int limit)
    {
        var shown = ranking.Ranked.Take(Math.Max(0, limit)).ToList();
        if (decision.SecondOpinionWorkId is { } id && limit >= 2 && shown.Count == limit && shown.All(w => w.Work.WorkId != id)
            && ranking.Ranked.FirstOrDefault(w => w.Work.WorkId == id) is { } pick)
            shown[^1] = pick;
        return shown;
    }

    private static StatusDecision CodeDecision(RankingResult ranking) => ranking.Status switch
    {
        _ when ranking.TopicLabel is { } topic => new StatusDecision(ranking.Status, ranking.Reason,
            topic.Length == 0 ? NoTopicHint : ranking.Status == MatchStatus.None ? TopicNoneHint : TopicHint),
        MatchStatus.Match => new StatusDecision(MatchStatus.Match, ranking.Reason, null),
        MatchStatus.None => new StatusDecision(MatchStatus.None, ranking.Reason, NoneHint),
        _ when ranking.Reason == StatusReason.AuthorOnly && ranking.Ladder.AuthorName is { } author
            => new StatusDecision(MatchStatus.Ambiguous, ranking.Reason, AuthorOnlyHint(author)),
        _ => new StatusDecision(MatchStatus.Ambiguous, ranking.Reason, AmbiguousHint),
    };

    private const int MaxNames = 5, MaxTitle = 200;
    private const string AlsoCredited = "also credited";

    private static string Cap(string s) => s.Length <= MaxTitle ? s : s[..MaxTitle];
}

/// <param name="SecondOpinionWorkId">The judge's pick when it differs from rank 1 (shown with second_opinion = true).</param>
public sealed record StatusDecision(MatchStatus Status, StatusReason Reason, string? Hint, string? SecondOpinionWorkId = null);
