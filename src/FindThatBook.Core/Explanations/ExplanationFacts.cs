using FindThatBook.Core.Model;

namespace FindThatBook.Core.Explanations;

/// <summary>
/// Validated facts for one candidate: the only input an explanation (LLM or template) may use.
/// <see cref="MatchPhrase"/> is code-owned match language; the LLM must not relabel the match (AI-32).
/// Every string comes from Open Library or from code; nothing the user typed is copied in (AI-29/AI-30):
/// <see cref="MatchedTerms"/> are title tokens the query also contained, so they are catalog text.
/// Built by <see cref="ExplanationFactsBuilder"/>.
/// </summary>
public sealed record ExplanationFacts
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string MatchPhrase { get; init; }

    /// <summary>What kind of match <see cref="MatchPhrase"/> states; the grounding check holds sentences to it. Never sent to the LLM.</summary>
    public required MatchKind Kind { get; init; }

    /// <summary>Display names, at most 3.</summary>
    public IReadOnlyList<string> PrimaryAuthors { get; init; } = [];

    /// <summary>
    /// At most 2, known roles only (AI-37): the contributor the query matched, then adapters of a tier A–C work.
    /// Other credits (narrators, translators, editors of some edition) are left out so no sentence can list them.
    /// </summary>
    public IReadOnlyList<ContributorFact> Contributors { get; init; } = [];

    /// <summary>The contributor whose credit matched the query (tier B, contributor-only): the sentence must name their role.</summary>
    public string? MatchedContributor { get; init; }

    /// <summary>
    /// Open Library's first_publish_year, set only when the query typed a year that matches it (DA-061:
    /// the field is unreliable, e.g. A Tale of Two Cities = 1800). Otherwise null and the sentence states no year; the
    /// response still shows the field. Known bad values (≤ 0, the 1800 sentinel) are never stated.
    /// </summary>
    public int? FirstPublishYear { get; init; }

    public int EditionCount { get; init; }
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];

    /// <summary>Character names as Open Library lists them (person field), at most 2.</summary>
    public IReadOnlyList<string> MatchedCharacters { get; init; } = [];

    /// <summary>The edition title that supplied a tier A–C title match (edition or original title evidence), when it differs from the work title (DE-06).</summary>
    public string? AlsoKnownAs { get; init; }

    /// <summary>"known in English as" when the edition title is the English title, else "also published as". Used verbatim.</summary>
    public string? AlsoKnownAsLabel { get; init; }

    /// <summary>An alternative name Open Library lists for a primary author that the query used (DA-007: pen names).</summary>
    public AuthorAliasFact? AuthorAlias { get; init; }

    /// <summary>The edition intent this work satisfies (the query asked for an adaptation or a study guide).</summary>
    public EditionIntent? Intent { get; init; }

    /// <summary>Duplicate Open Library records folded into this work (DA-056).</summary>
    public int DuplicateRecords { get; init; }

    /// <summary>Topic requests: subject headings as Open Library lists them (main heading only, at most 2), quoted verbatim.</summary>
    public IReadOnlyList<string> MatchedSubjects { get; init; } = [];
}

public sealed record ContributorFact(string Name, string Role);

/// <param name="Alias">The alternative name ("Richard Bachman").</param>
/// <param name="Of">The primary author it belongs to ("Stephen King"), one of <see cref="ExplanationFacts.PrimaryAuthors"/>.</param>
public sealed record AuthorAliasFact(string Alias, string Of);

/// <summary>The match a sentence must state. Each kind has one code-owned phrase (<see cref="ExplanationFactsBuilder.PhraseFor"/>).</summary>
public enum MatchKind
{
    /// <summary>Tier A/B: the typed (or edition) title matches exactly.</summary>
    ExactTitle,
    /// <summary>Tier A/B where the exact title came from the interpreter's guess, not from typed words.</summary>
    InferredTitle,
    /// <summary>Tier A/B where the work's own title is the original-language title the interpreter named.</summary>
    OriginalTitle,
    /// <summary>Tier C.</summary>
    NearTitle,
    /// <summary>Tier D through a primary author.</summary>
    AuthorOnly,
    /// <summary>Tier D through a contributor credit (illustrator, adapter…).</summary>
    ContributorOnly,
    /// <summary>Tier E with character evidence.</summary>
    Character,
    /// <summary>Tier E with an exact title, but the query named an author who is not credited.</summary>
    TitleOnly,
    /// <summary>Tier E with some title words.</summary>
    PartialTitle,
    /// <summary>Tier E with no title, author or character evidence.</summary>
    Weak,
    /// <summary>Topic request: Open Library lists subject headings that contain the topic.</summary>
    Topic,
    /// <summary>Topic request: a title the interpreter suggested and Open Library has, with no subject evidence; the sentence claims nothing about its topic.</summary>
    Suggested,
}
