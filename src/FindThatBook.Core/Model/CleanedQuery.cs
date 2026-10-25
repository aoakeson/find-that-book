namespace FindThatBook.Core.Model;

/// <summary>
/// The validated, cleaned query (01 §1 + 04/06 amendments). Tokens are folded for comparison;
/// <see cref="Search"/> is what the broad Open Library call sends.
/// </summary>
public sealed record CleanedQuery
{
    /// <summary>The user's text after validation (trimmed, control/format characters removed).</summary>
    public required string Raw { get; init; }

    /// <summary>Raw after markup cleanup (HTML entities, markdown links, URL slugs); tokens come from this.</summary>
    public required string Text { get; init; }

    /// <summary>Every folded token, in typed order.</summary>
    public required IReadOnlyList<string> All { get; init; }

    /// <summary>Content tokens (not stop/noise/extracted year), digit form, in typed order.</summary>
    public required IReadOnlyList<string> Search { get; init; }

    /// <summary>Stop, noise and extracted-year tokens: never sent, but count as matched when a title contains them.</summary>
    public required IReadOnlySet<string> Optional { get; init; }

    /// <summary>Content tokens typed before a bare "or" (subtitle variant), else empty.</summary>
    public required IReadOnlyList<string> Head { get; init; }

    /// <summary>Extracted publication year (only when at least two other content tokens remain).</summary>
    public int? Year { get; init; }

    /// <summary>Year-like tokens kept as content because too few other words remain (IN-044): sent, but optional for title exactness.</summary>
    public required IReadOnlySet<string> SoftTokens { get; init; }

    /// <summary>Edition intents (graphic novel, study guide…) found in the query.</summary>
    public required IReadOnlySet<EditionIntent> Intents { get; init; }

    /// <summary>Format/noise words removed from the search text, for the response.</summary>
    public required IReadOnlyList<string> RemovedNoise { get; init; }

    /// <summary>
    /// Leading request words ("find me", "can you recommend") taken out of <see cref="Search"/>: optional for exactness,
    /// like noise words, so "find me aciman" still matches Aciman's *Find Me*.
    /// </summary>
    public IReadOnlySet<string> RequestTokens { get; init; } = new HashSet<string>();

    /// <summary>
    /// Topic phrases when the text is a topic request, found without the LLM; empty otherwise. One entry per phrase:
    /// "a book about the french revolution" → ["french revolution"]; "books about bears and wolves" → ["bears", "wolves"].
    /// The interpreter's intent and subjects take precedence when it answers.
    /// </summary>
    public IReadOnlyList<string> Topic { get; init; } = [];

    /// <summary>
    /// Audience or form words typed with a topic ("for kids", "picture books", "a novel"), as subject words to prefer
    /// ("juvenile", "picture books", "fiction"). They order results; they never filter or retrieve on their own.
    /// </summary>
    public IReadOnlyList<string> TopicPreferences { get; init; } = [];

    /// <summary>Author words typed after "by" in a topic request ("books about cats by doris lessing"), else null.</summary>
    public string? TopicAuthor { get; init; }

    public bool IsEmpty => Search.Count == 0;
}
