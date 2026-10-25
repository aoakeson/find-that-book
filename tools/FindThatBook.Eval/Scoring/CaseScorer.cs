using FindThatBook.Core.Normalization;
using FindThatBook.Eval.Datasets;

namespace FindThatBook.Eval.Scoring;

/// <summary>One shown candidate, reduced to what scoring needs.</summary>
/// <param name="FoldedIds">Duplicate work ids the ranker folded into this one (counted only by sets that ask for it: battle).</param>
/// <param name="Subjects">The work's subjects as the pipeline exposes them (topic rows); empty when not exposed.</param>
public sealed record ObservedWork(string WorkId, string Title, IReadOnlyList<string> AuthorNames, IReadOnlyList<string> AuthorKeys,
    IReadOnlyList<string>? FoldedIds = null, IReadOnlyList<string>? Subjects = null)
{
    public static ObservedWork Of(string workId, string title = "", IReadOnlyList<string>? names = null, IReadOnlyList<string>? keys = null,
        IReadOnlyList<string>? folded = null, IReadOnlyList<string>? subjects = null) => new(workId, title, names ?? [], keys ?? [], folded, subjects);
}

/// <param name="Outcome">match | ambiguous | none | rejected | error (<see cref="Outcomes"/>).</param>
/// <param name="ErrorCode">The validator's code when rejected; the exception kind when error.</param>
/// <param name="Shown">The candidates the API would return, in order.</param>
/// <param name="Reason">The status reason, snake_case (topic rows require "topic_search").</param>
public sealed record Observed(string Outcome, string? ErrorCode, IReadOnlyList<ObservedWork> Shown, string? Reason = null);

/// <summary>Pass flags for one row. Null = not scored for this row.</summary>
/// <param name="TargetRank">1-based rank of the first acceptable candidate among the shown ones, or null.</param>
public sealed record CaseScore(
    bool Answerable,
    bool? Top1,
    bool? Top5,
    bool? Status,
    bool IsMatch,
    bool? MatchCorrect,
    bool FalseMatch,
    int? TargetRank,
    bool DupRecordMatch = false)
{
    public bool Passed => Top1 != false && Status != false;

    /// <summary>
    /// judge.py's severe matches: a "match" on the wrong book (wrong_match) or on a row with no answer (false_match).
    /// Not severe: a duplicate record of the right book, or the right book returned as match where the row expected
    /// ambiguous (status_over, a status error only).
    /// </summary>
    public bool SevereMatch => IsMatch && (FalseMatch || (Answerable && Top1 == false && !DupRecordMatch));
}

/// <summary>
/// The golden scoring block, as code (eval/golden.v2.json "scoring"; held-out and lenient jsonl variants):
/// top-1 = shown[0] acceptable; top-5 = any of shown[0..4]; status = outcome in expected statuses; a match is correct
/// only if top-1 is right and the row allows match; any match on a row with no answer is a false positive.
/// Names and titles are keyed with the pipeline's own normalization (PersonNames, TitleText), so eval and pipeline
/// fold text identically (one source of truth for text folding).
/// </summary>
public static class CaseScorer
{
    public static CaseScore Score(Expectation e, Observed o)
    {
        var isMatch = o.Outcome == Outcomes.Match;
        if (!e.Answerable)
        {
            var statusOk = e.Statuses.Count == 0 ? (bool?)null : StatusOk(e, o);
            return new CaseScore(false, null, null, statusOk, isMatch, isMatch ? false : null, isMatch, null);
        }

        var shown = o.Shown.Take(5).ToList();
        if (e.IsTopicRow)
        {
            // A topic request has no single right book: it passes on status + reason and on how many shown works are on topic.
            var hits = shown.Select(w => TopicHit(e, w)).ToList();
            bool? topicStatus = e.Statuses.Count == 0 ? null : StatusOk(e, o);
            return new CaseScore(true, hits.Count > 0 && hits[0], hits.Count(h => h) >= e.MinTopicHits, topicStatus, isMatch,
                isMatch ? false : null, false, hits.IndexOf(true) is >= 0 and var first ? first + 1 : null);
        }
        int? rank = null;
        for (var i = 0; i < shown.Count; i++)
        {
            if (!Hit(e, shown[i], top1: false)) continue;
            rank = i + 1;
            break;
        }
        var top1 = shown.Count > 0 && Hit(e, shown[0], top1: true);
        var top5 = rank is not null;
        bool? status = e.Statuses.Count == 0 ? null : StatusOk(e, o) && !(e.WrongMatchIsStatusError && isMatch && !top1);
        bool? matchCorrect = isMatch ? top1 && e.AllowsMatch : null;
        // battle judge.py: a match on another record of the intended book (same title key + author) is dup_record_match.
        var dup = isMatch && !top1 && shown.Count > 0 && SameBook(e, shown[0]);
        return new CaseScore(true, top1, top5, status, isMatch, matchCorrect, false, rank, dup);
    }

    private static bool StatusOk(Expectation e, Observed o)
    {
        if (!e.Statuses.Contains(o.Outcome)) return false;
        if (e.ExpectedReason is { } reason && o.Reason != reason) return false;
        return o.Outcome != Outcomes.Rejected || e.ErrorCode is null || e.ErrorCode == o.ErrorCode;
    }

    /// <summary>Is <paramref name="w"/> an acceptable answer? Top-1 and top-5 may accept different id lists (golden contrib-02).</summary>
    public static bool Hit(Expectation e, ObservedWork w, bool top1)
    {
        var ids = top1 ? e.Top1 : e.Top5;
        if (ids.Contains(w.WorkId, StringComparer.Ordinal)) return true;
        if (e.CountFolded && w.FoldedIds is { Count: > 0 } folded && folded.Any(f => ids.Contains(f, StringComparer.Ordinal))) return true;
        if (e.IsAuthorRow) return AuthorHit(e, w);
        if (e.LenientTitle is not null && e.LenientAuthor is not null)
        {
            // The research prototype's eval.py rule: a duplicate record of the same book (same main-title key, same author) counts.
            var key = PersonNames.Key(e.LenientAuthor);
            return LenientTitleKey(w.Title) == LenientTitleKey(e.LenientTitle) && w.AuthorNames.Any(n => PersonNames.Key(n) == key);
        }
        return false;
    }

    private static bool AuthorHit(Expectation e, ObservedWork w)
    {
        if (e.AuthorKeys.Count > 0) return w.AuthorKeys.Any(k => e.AuthorKeys.Contains(k, StringComparer.Ordinal));
        if (e.AuthorSurname is { } surname) return w.AuthorNames.Any(n => PersonNames.Surname(n) == surname);
        var keys = e.AuthorNames.Select(PersonNames.Key).ToHashSet(StringComparer.Ordinal);
        return w.AuthorNames.Any(n => keys.Contains(PersonNames.Key(n)));
    }

    /// <summary>Does one of the work's subjects match a topic word? (folded; word + optional s/es, "word*" = prefix, phrases as phrases)</summary>
    public static bool TopicHit(Expectation e, ObservedWork w)
    {
        if (w.Subjects is not { Count: > 0 } subjects) return false;
        foreach (var subject in subjects)
        {
            var text = " " + string.Join(' ', TextFolding.Tokens(subject)) + " ";
            foreach (var word in e.TopicWords)
            {
                var prefix = word.EndsWith('*');
                var phrase = string.Join(' ', TextFolding.Tokens(word.TrimEnd('*')));
                if (phrase.Length == 0) continue;
                if (prefix ? text.Contains(" " + phrase, StringComparison.Ordinal)
                           : text.Contains($" {phrase} ", StringComparison.Ordinal) || text.Contains($" {phrase}s ", StringComparison.Ordinal)
                             || text.Contains($" {phrase}es ", StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Same main-title key as one of the intended titles and an author among the intended names (judge.py same_book, approximated).</summary>
    public static bool SameBook(Expectation e, ObservedWork w)
    {
        if (e.DupTitles.Count == 0 || e.DupAuthors.Count == 0) return false;
        var titles = e.DupTitles.Select(TitleText.Key).ToHashSet(StringComparer.Ordinal);
        if (!titles.Contains(TitleText.Key(w.Title))) return false;
        var authors = e.DupAuthors.Select(PersonNames.Key).ToHashSet(StringComparer.Ordinal);
        var surnames = e.DupAuthors.Select(PersonNames.Surname).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        return w.AuthorNames.Any(n => authors.Contains(PersonNames.Key(n)) || surnames.Contains(PersonNames.Surname(n)));
    }

    /// <summary>The research prototype's eval.py strip(): the title key without format/noise words ("a separate peace novel" = "a separate peace").</summary>
    public static string LenientTitleKey(string title) =>
        string.Join(' ', TitleText.Key(title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Vocabulary.Noise.Contains(t)));
}
