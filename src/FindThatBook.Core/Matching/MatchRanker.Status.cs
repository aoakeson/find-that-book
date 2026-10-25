using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// The status code alone would give (no judge). <c>none</c>: the top is tier E with a weak title and no character
    /// evidence. <c>match</c>: the top is A/B, none of the guards below fires, no lower-tier work outscores it by the
    /// margin, and it leads the next work of its tier by the margin. Otherwise <c>ambiguous</c>, with the reason.
    /// <see cref="StatusReason.CloseScores"/> is the only ambiguous outcome a confident, agreeing judge may lift.
    /// </summary>
    private static (MatchStatus, StatusReason) CodeStatus(List<Candidate> ranked, RankingContext context, Mashup? mashup)
    {
        var top = ranked[0];
        var options = context.Options;
        if (context.TopicMode)
        {
            // A topic request is a list of books about the topic, never one matched book. No topic at all, or no work
            // with topic evidence (a misspelled or unknown subject), is "none" with no candidates.
            var anyEvidence = ranked.Any(w => w.MatchedSubjects.Count > 0 || w.SuggestedByInterpreter);
            return TopicLabel(context).Length > 0 && anyEvidence
                ? (MatchStatus.Ambiguous, StatusReason.TopicSearch)
                : (MatchStatus.None, StatusReason.NoCandidates);
        }
        if (top.Tier == MatchTier.E && top.TitleScore < 0.5 && top.CharacterCoverage < 0.5)
        {
            // R3-0390: popular OR-search works can outscore an exact title by the typed author further down; that is not
            // "nothing fits".
            var authored = options.NoneGuard && ranked.Take(5).Any(w => w.Tier is MatchTier.A or MatchTier.B && w.Role is not null);
            return (authored ? MatchStatus.Ambiguous : MatchStatus.None, StatusReason.WeakEvidence);
        }
        if (top.Tier is MatchTier.A or MatchTier.B)
        {
            // An interpreted title no typed word supports may rank, but never auto-matches.
            if (context.Hypothesis is { } hypothesis && !hypothesis.IsGroundedIn(context.Query) && !top.ExactTyped)
                return (MatchStatus.Ambiguous, StatusReason.UnsupportedGuess);
            if (options.CharacterWord && LoneCharacterWord(top, context))
                return (MatchStatus.Ambiguous, StatusReason.WeakEvidence);
            if (options.LoneSurname && LoneSurname(top, context))
                return (MatchStatus.Ambiguous, StatusReason.WeakEvidence);
            if (options.SeriesName && SeriesName(ranked, context))
                return (MatchStatus.Ambiguous, StatusReason.WeakEvidence);
            if (options.TinyRecord && TinyRecordTop(ranked))
                return (MatchStatus.Ambiguous, StatusReason.WeakEvidence);
            if (mashup is not null)
                return (MatchStatus.Ambiguous, StatusReason.UnsupportedGuess);
            // An edition-intent top (the graphic novel the query asks for) is compared only with other intent hits: the
            // canonical work it was sorted above is a different request, not a rival reading.
            var rivals = ranked.Skip(1).Where(w => !options.IntentRivals || !top.IntentHit || w.IntentHit).ToList();
            // A lower-tier work that is not ruled out by a typed author outscores the A/B top by the margin: tier and
            // score disagree.
            if (rivals.Any(w => w.Tier is not (MatchTier.A or MatchTier.B) && !w.Foreign && !w.ExcludedByContributor
                                && w.Score > top.Score + Weights.MatchMargin))
                return (MatchStatus.Ambiguous, StatusReason.ScoreConflict);
            if (options.SameTitleRival && SameTitleByAnotherAuthor(top, rivals))
                return (MatchStatus.Ambiguous, StatusReason.CloseScores);
            var sameTier = rivals.FirstOrDefault(w => w.Tier == top.Tier);
            if (sameTier is null || top.Score - sameTier.Score >= Weights.MatchMargin)
                return (MatchStatus.Match, MatchReason(top));
            return (MatchStatus.Ambiguous, StatusReason.CloseScores);
        }
        return top.Tier switch
        {
            MatchTier.C => (MatchStatus.Ambiguous, StatusReason.NearTitleAuthor),
            MatchTier.D => (MatchStatus.Ambiguous, StatusReason.AuthorOnly),
            _ => (MatchStatus.Ambiguous, StatusReason.WeakEvidence),
        };
    }

    /// <summary>
    /// DA-003 ("the talisman": Scott's, 77 editions, and King &amp; Straub's, 66): with no author typed, another work with the
    /// same exact title by a different author and ≥ 25% of the top's editions means two real books share the title. An
    /// interpreted author alone does not settle it (it is a guess, not what the user typed); a typed author does.
    /// </summary>
    private static bool SameTitleByAnotherAuthor(Candidate top, List<Candidate> rivals)
        => top.AuthorTokens.Count == 0
           && rivals.Any(w => w.Exact && w.TitleKey == top.TitleKey && w.Authors.Count > 0 && top.Authors.Count > 0
                              && !w.SharesAuthorWith(top) && !w.Foreign && !w.ExcludedByContributor
                              && w.Editions >= Weights.SameTitleRivalShare * top.Editions);

    private static StatusReason MatchReason(Candidate top) => top.Tier == MatchTier.B ? StatusReason.ExactTitleContributorAuthor
        : top.Role == AuthorRole.Primary ? StatusReason.ExactTitlePrimaryAuthor : StatusReason.ExactTitle;

    /// <summary>
    /// DE-01 / AI-43: the query is one word the interpreter reads as a character name and no author is typed. The top is
    /// either a title collision (*HERmione* by H.D. for "hermione": not the interpreter's book) or a book inferred from the
    /// character (Philosopher's Stone, exact only through the interpreted title): never a match. A typed title that is
    /// also the interpreter's book ("frankenstein", "emma") is unaffected; "katniss" loses its match (DE-01 wins).
    /// </summary>
    private static bool LoneCharacterWord(Candidate top, RankingContext context)
    {
        if (context.Hypothesis is not { } hypothesis || context.Search.Count != 1 || top.AuthorTokens.Count > 0) return false;
        return context.HypothesisCharacterTokens.Contains(context.Search[0]) && (!top.ExactTyped || !IsHypothesisTitle(top, hypothesis));
    }

    /// <summary>
    /// IN-058 / DA-047: the typed title is a strict prefix of the top's title and of ≥ 2 other works by the same author
    /// ("harry potter" → seven books): a series name, not one work. Not when the typed words are the top's complete
    /// title ("dune", "twilight").
    /// </summary>
    private static bool SeriesName(List<Candidate> ranked, RankingContext context)
    {
        var top = ranked[0];
        if (top.ExactTyped || top.Primary.Count == 0) return false;
        var typed = top.NonAuthorTokens.Where(t => !context.SoftTokens.Contains(t)).ToList();
        if (typed.Count == 0) return false;
        bool StartsWithTyped(Candidate work) => work.Main.Count > typed.Count
            && typed.Select((t, i) => TokenMatch.Equal(t, work.Main[i])).All(equal => equal);
        if (!StartsWithTyped(top)) return false;
        return ranked.Skip(1).Count(w => w.TitleKey != top.TitleKey && w.Primary.Overlaps(top.Primary) && StartsWithTyped(w)) >= 2;
    }

    /// <summary>
    /// DA-009 / DA-016 (tiny records): a ≤ 2-edition top never matches when another work with ≥ 10× its editions has a
    /// title the top's title contains ("Nancy Drew in The Secret of the old clock", 1 edition, vs *The Secret of the Old
    /// Clock*, 41). An edition-intent top is exempt.
    /// </summary>
    private static bool TinyRecordTop(List<Candidate> ranked)
    {
        var top = ranked[0];
        if (top.Editions > Weights.TinyMaxEditions || top.IntentHit) return false;
        var words = top.Full.Concat(top.EditionSets.SelectMany(e => e.Full)).ToList();
        return ranked.Skip(1).Any(w => w.Editions >= Weights.TinyFactor * Math.Max(top.Editions, 1) && w.Main.Count > 0
                                       && w.Main.All(t => words.Any(word => TokenMatch.Equal(t, word))));
    }

    private static bool IsHypothesisTitle(Candidate work, NormalizedHypothesis hypothesis)
    {
        if (!hypothesis.HasTitle) return false;
        IReadOnlyList<string>[] titles = [work.Main, work.Full, .. work.EditionSets.SelectMany(e => new[] { e.Main, e.Full })];
        return titles.Any(t => TokenMatch.Score(hypothesis.Main, t).Exact || TokenMatch.Score(hypothesis.Full, t).Exact);
    }

    /// <summary>
    /// R3-0025 ("rose windy", no interpreter): the only author evidence is one typed word equal to the surname of an author
    /// with fewer than 3 editions in the pool, so a title + "author" reading of two character names is not a match.
    /// </summary>
    private static bool LoneSurname(Candidate top, RankingContext context)
    {
        if (context.Hypothesis is not null || top.Role != AuthorRole.Primary || top.AuthorTokens.Count != 1 || top.AuthorFromHypothesis) return false;
        var keys = top.AuthorKeys;
        var authorEditions = context.Works.Where(w => w.Authors.Any(a => keys.Contains(a.NameKey))).Sum(w => (long)w.Editions);
        return authorEditions < 3;
    }
}
