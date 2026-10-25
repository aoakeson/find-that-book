namespace FindThatBook.Core.Model;

/// <summary>An author as shown to the user. <see cref="Role"/> is set for contributors ("adapter", "illustrator").</summary>
public sealed record AuthorCredit(string Name, string? Key, string? Role = null);

public enum TitleSource { None, Typed, Hypothesis, OriginalTitle, EditionTitle }

/// <summary>The facts the matcher established for one candidate. Explanations are built only from these.</summary>
public sealed record MatchEvidence
{
    public bool ExactTitle { get; init; }
    public double TitleScore { get; init; }
    public TitleSource TitleSource { get; init; }

    /// <summary>Typed query tokens found in the work's title (folded).</summary>
    public IReadOnlyList<string> MatchedTitleTerms { get; init; } = [];

    public AuthorRole? Role { get; init; }

    /// <summary>Display name of the matched author (primary or contributor).</summary>
    public string? MatchedAuthor { get; init; }
    public string? MatchedContributorRole { get; init; }
    public bool AuthorFromHypothesis { get; init; }
    public bool AuthorViaAlternativeName { get; init; }

    public double CharacterCoverage { get; init; }
    public IReadOnlyList<string> MatchedCharacters { get; init; } = [];

    public bool IntentMatch { get; init; }
    public bool YearMatched { get; init; }

    /// <summary>The edition title that supplied the title match (e.g. the English title of a translation).</summary>
    public string? MatchedEditionTitle { get; init; }

    /// <summary>The query names an author who is not on this work.</summary>
    public bool ForeignAuthor { get; init; }

    /// <summary>Marker penalties applied: "omnibus", "study", "adapt".</summary>
    public IReadOnlyList<string> Penalties { get; init; } = [];

    /// <summary>Topic requests: this work's subject headings that contain a topic word (catalog-grounded).</summary>
    public IReadOnlyList<string> MatchedSubjects { get; init; } = [];

    /// <summary>Topic requests: the interpreter suggested this title (and Open Library has it).</summary>
    public bool SuggestedByInterpreter { get; init; }
}

/// <summary>A deduplicated, scored candidate in final order.</summary>
public sealed record RankedWork
{
    public required CandidateWork Work { get; init; }
    public required MatchTier Tier { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<AuthorCredit> PrimaryAuthors { get; init; }
    public required IReadOnlyList<AuthorCredit> Contributors { get; init; }
    public required MatchEvidence Evidence { get; init; }

    /// <summary>Other work ids folded into this one as duplicates.</summary>
    public IReadOnlyList<string> FoldedWorkIds { get; init; } = [];
}
