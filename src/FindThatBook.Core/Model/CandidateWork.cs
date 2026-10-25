namespace FindThatBook.Core.Model;

/// <summary>One Open Library work as returned by search.json, already mapped out of the vendor DTO.</summary>
public sealed record CandidateWork
{
    /// <summary>Bare work id, e.g. "OL27482W".</summary>
    public required string WorkId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }

    /// <summary>author_name, in Open Library order. Order carries no meaning (01 Q2): never read the first as primary.</summary>
    public IReadOnlyList<string> AuthorNames { get; init; } = [];

    /// <summary>Bare author keys aligned with <see cref="AuthorNames"/> (may be shorter).</summary>
    public IReadOnlyList<string> AuthorKeys { get; init; } = [];

    /// <summary>Raw contributor strings, e.g. "Charles Dixon (Adapter)".</summary>
    public IReadOnlyList<string> Contributors { get; init; } = [];

    public int? FirstPublishYear { get; init; }
    public int EditionCount { get; init; }

    /// <summary>Cover id; null when absent or ≤ 0.</summary>
    public int? CoverId { get; init; }
    public int ReadingLogCount { get; init; }

    /// <summary>
    /// Bare key of the edition Open Library matched best for this search (editions.docs[0].key), e.g. "OL25760876M";
    /// null when absent. Links feature it, so the work page opens on that edition rather than one Open Library picks
    /// (for the Book of Mormon, a Spanish one).
    /// </summary>
    public string? EditionKey { get; init; }

    /// <summary>editions.title of the best-matching edition(s) (≤ 3), e.g. the English title of a translated work.</summary>
    public IReadOnlyList<string> EditionTitles { get; init; } = [];

    /// <summary>author_alternative_name (pen names, real names, other scripts), capped.</summary>
    public IReadOnlyList<string> AuthorAlternativeNames { get; init; } = [];

    /// <summary>Character names (person field); only filled for works seen in a person: call.</summary>
    public IReadOnlyList<string> Persons { get; init; } = [];

    /// <summary>Subject headings (subject field); only filled for works seen in a topic call.</summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];
}
