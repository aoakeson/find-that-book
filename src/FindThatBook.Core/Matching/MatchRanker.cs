using FindThatBook.Core.Model;

namespace FindThatBook.Core.Matching;

/// <summary>
/// Ranks the catalog pool for one query: resolve each work's primary authors and contributors → classify (tier and
/// evidence) → score → order → fold duplicates → post-sort rules → code status. Tiers label the evidence; one score orders
/// every work, with a guarded exact-primary precedence after scoring. Deterministic: the same query, hypothesis and pool always give the
/// same result, whatever the pool order; ties fall to edition count, then work id (ordinal).
/// <para>
/// <b>Score</b> (weights in <see cref="RankingWeights"/>):
/// 4.0 × title similarity (1.0 when exact)
/// + 2.0 × author role (primary 1, contributor 0.5)
/// + 2.5 × log10(1 + edition count)
/// + 0.5 if the first publication year is within a year of the query's year
/// − marker penalties (omnibus 1.5, study guide 2.5, adaptation 1.5, volume part 1.5; each waived when the query asks for it)
/// + 4.0 × character coverage (share of typed non-author words found among the work's character names)
/// + 0.5 × log10(1 + reading-log count)
/// − 3.0 when the query names another author and no author of this work matched.
/// </para>
/// <para>
/// <b>Order</b>: works that satisfy an edition intent (graphic novel, study guide) first, then score (+2.0 for tier B),
/// then edition count, then work id. <b>Post-sort rules</b>, in order:
/// exact title by a typed author above keyword-only works (R3-0390) ·
/// exact title above a much more popular partial title by another author (IN-050 "it") ·
/// a work naming a typed contributor above the same-title work that leaves that name unexplained (golden contrib-03) ·
/// a ≤ 2-edition top gives way to a same-title work with 10× its editions (DA-009) ·
/// a typed exact primary title with at least 10 editions precedes near-title and author-only hits ·
/// a title mashed from two works by one author shows both first (R3-0195).
/// </para>
/// Rules added on top of the measured baseline can each be switched off through <see cref="RankerOptions"/>.
/// </summary>
public static partial class MatchRanker
{
    private static readonly RankingWeights Weights = RankingWeights.Measured;

    /// <summary>Deterministic for identical inputs: same query, hypothesis and pool → same result.</summary>
    public static RankingResult Rank(CleanedQuery query, QueryHypothesis? hypothesis, IReadOnlyList<CandidateWork> pool)
        => Rank(query, hypothesis, pool, RankerOptions.Default);

    internal static RankingResult Rank(CleanedQuery query, QueryHypothesis? hypothesis, IReadOnlyList<CandidateWork> pool, RankerOptions options)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var works = new List<Candidate>();
        foreach (var work in pool)
            if (seenIds.Add(work.WorkId)) works.Add(new Candidate(work, works.Count));
        if (works.Count == 0)
        {
            // Nothing about the topic (or no topic at all: "recommend me a good book") still gets a topic hint.
            return options.Topics && TopicRequest.From(query, hypothesis) is { } topic
                ? RankingResult.Empty with { TopicLabel = TopicTerms.Label(topic) }
                : RankingResult.Empty;
        }

        ResolveRoles(works, options);
        var context = new RankingContext(query, hypothesis, NormalizedHypothesis.From(hypothesis), options, works);
        foreach (var work in works) Classify(work, context);
        if (context.Topic is not null) ApplyTopicOrder(works, context);

        var ranked = FoldDuplicates(Order(works), options, context.TopicMode);
        if (context.TopicMode)
        {
            ranked = ranked.Where(w => ShownForTopic(w, context)).ToList();
            if (ranked.Count == 0) return RankingResult.Empty with { TopicLabel = TopicLabel(context) };
        }
        Mashup? mashup = null;
        if (!context.TopicMode)
        {
            if (options.AuthoredExact) LiftExactTitlesByTypedAuthors(ranked);
            if (options.ExactOverPartial) LiftExactTitlesOverPartialTitles(ranked);
            if (options.NamedContributor) LiftWorksNamingTypedContributors(ranked);
            PromoteLargerSameTitlePeer(ranked, options);
            if (options.ExactPrimaryPrecedence) LiftExactPrimaryAboveNearAndAuthorOnly(ranked);
            mashup = options.Mashup ? Mashup.Find(ranked, context) : null;
        }
        if (mashup?.Sibling is { } sibling && ranked.IndexOf(sibling) > 1)
        {
            ranked.Remove(sibling);
            ranked.Insert(1, sibling);
        }

        var (status, reason) = CodeStatus(ranked, context, mashup);
        var top = ranked[0];
        return new RankingResult([.. ranked.Select(work => ToRankedWork(work, context))], status, reason)
        {
            Ladder = LadderSignalsFor(ranked, context, mashup),
            TopicLabel = context.TopicMode ? TopicLabel(context) : null,
            Facts = new StatusFacts { SingleTokenNoAuthor = query.Search.Count == 1 && top.AuthorTokens.Count == 0 },
        };
    }
}
