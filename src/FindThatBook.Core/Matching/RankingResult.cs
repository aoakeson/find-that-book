using FindThatBook.Core.Model;

namespace FindThatBook.Core.Matching;

/// <summary>
/// Ranked, deduplicated candidates plus the code-only status, with the facts the retrieval ladder and the status policy
/// read. Members may be added, never removed.
/// </summary>
public sealed record RankingResult(IReadOnlyList<RankedWork> Ranked, MatchStatus Status, StatusReason Reason)
{
    public static RankingResult Empty { get; } = new([], MatchStatus.None, StatusReason.NoCandidates);

    /// <summary>What the retrieval ladder decides on.</summary>
    public LadderSignals Ladder { get; init; } = LadderSignals.None;

    /// <summary>
    /// The topic ("bears") when this ranking answered a topic request ("" when the request names none); null for a book
    /// search, including a topic-shaped request whose text is a book's title.
    /// </summary>
    public string? TopicLabel { get; init; }

    /// <summary>What the status policy needs to combine the code status with the judge.</summary>
    public StatusFacts Facts { get; init; } = StatusFacts.None;
}

/// <summary>Ranking facts the retrieval ladder needs; computed by <see cref="MatchRanker"/> on the final order.</summary>
public sealed record LadderSignals
{
    public static LadderSignals None { get; } = new();

    /// <summary>The top is A–C (or the query is author-only) and the pool has ≥ 3 works: the weak-pool searches are skipped.</summary>
    public bool Strong { get; init; }

    /// <summary>No A–C work, and some D work's authors explain every typed word, one of them by surname.</summary>
    public bool AuthorOnly { get; init; }

    /// <summary>Some candidate is tier A or B.</summary>
    public bool HasExactTier { get; init; }

    /// <summary>Author key for the "most-published works" search; null when none fits.</summary>
    public string? TopWorksAuthorKey { get; init; }

    /// <summary>Display name of the author an author-only query names (for the hint).</summary>
    public string? AuthorName { get; init; }

    /// <summary>Leftover typed words to retry as character names (1–3 words), or empty when that fallback does not apply.</summary>
    public IReadOnlyList<string> CharacterFallbackTokens { get; init; } = [];

    /// <summary>R3-0195: author surname + typed title words another work by the top's author holds; one OR search looks for that work.</summary>
    public IReadOnlyList<string> MashupProbeTokens { get; init; } = [];
}

/// <summary>Facts behind the code status that limit what the judge may change (AI-43).</summary>
public sealed record StatusFacts
{
    public static StatusFacts None { get; } = new();

    /// <summary>One typed search token and no typed author (AI-43): the judge can confirm a code match but never create one.</summary>
    public bool SingleTokenNoAuthor { get; init; }
}
