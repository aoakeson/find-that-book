namespace FindThatBook.Core.Matching;

/// <summary>
/// The ranking constants. Measured on the research golden and bulk sets; change them only with a new measurement.
/// </summary>
internal sealed record RankingWeights
{
    public static RankingWeights Measured { get; } = new();

    // ---- score terms (see the formula on MatchRanker) ----

    /// <summary>× title similarity (1.0 for an exact title).</summary>
    public double Title { get; init; } = 4.0;

    /// <summary>× author role: 1 for a primary author, 0.5 for a contributor.</summary>
    public double AuthorRole { get; init; } = 2.0;

    /// <summary>× log10(1 + edition count): the popularity prior.</summary>
    public double Editions { get; init; } = 2.5;

    /// <summary>When the first publication year is within one year of the query's year.</summary>
    public double Year { get; init; } = 0.5;

    /// <summary>× share of the typed non-author words found among the work's character names.</summary>
    public double Characters { get; init; } = 4.0;

    /// <summary>× log10(1 + reading-log count).</summary>
    public double ReadingLog { get; init; } = 0.5;

    /// <summary>Subtracted when the query names an author who is not on this work and no author of it matched.</summary>
    public double OtherAuthorPenalty { get; init; } = 3.0;

    public double OmnibusPenalty { get; init; } = 1.5;
    public double StudyGuidePenalty { get; init; } = 2.5;
    public double AdaptationPenalty { get; init; } = 1.5;
    public double VolumePartPenalty { get; init; } = 1.5;

    /// <summary>Topic requests: × share of the topic terms the work's subject headings contain.</summary>
    public double TopicSubjects { get; init; } = 4.0;

    /// <summary>Topic requests: × share of the other (broader) topics the work's subject headings contain.</summary>
    public double TopicSecondarySubjects { get; init; } = 1.0;

    /// <summary>Topic requests: a book the interpreter suggested and the catalog has.</summary>
    public double TopicSuggestion { get; init; } = 2.0;

    /// <summary>Topic requests: a subject heading holds a preferred audience or form ("juvenile", "picture books"). Orders, never filters.</summary>
    public double TopicPreference { get; init; } = 1.0;

    // ---- ordering and thresholds ----

    /// <summary>Added to a tier-B work's score when ordering (an exact title with a matched contributor).</summary>
    public double TierBBonus { get; init; } = 2.0;

    /// <summary>A near title needs this share of the query words matched…</summary>
    public double NearPrecision { get; init; } = 0.6;

    /// <summary>…and this share of the title words matched.</summary>
    public double NearRecall { get; init; } = 0.3;

    /// <summary>Score gap the top needs over the next work of its tier (and that a lower-tier work must exceed) for a match.</summary>
    public double MatchMargin { get; init; } = 0.5;

    /// <summary>A typed author name whose author holds this share of the pool's editions explains away other works.</summary>
    public double DominantAuthorShare { get; init; } = 0.3;

    /// <summary>A query that is one author's full name, when that author holds this share of the pool's editions.</summary>
    public double AuthorPhraseShare { get; init; } = 0.1;

    /// <summary>A same-title work by another author with this share of the top's editions makes the title ambiguous (DA-003).</summary>
    public double SameTitleRivalShare { get; init; } = 0.25;

    /// <summary>A top with at most this many editions…</summary>
    public int TinyMaxEditions { get; init; } = 2;

    /// <summary>…gives way to a same-title work with this many times its editions.</summary>
    public double TinyFactor { get; init; } = 10.0;

    /// <summary>Fewer works than this is a weak pool: the fallback searches run.</summary>
    public int WeakPoolSize { get; init; } = 3;
}
