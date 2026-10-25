using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;

namespace FindThatBook.Api.Contracts;

/// <summary>
/// The search answer: 200 for match, ambiguous and none alike ("no book matched" is an answer, not an error).
/// All fields are always present; unknown values are null, never guessed.
/// </summary>
public sealed record SearchResponse
{
    /// <summary>The query as searched: trimmed, with control and zero-width characters removed.</summary>
    public required string Query { get; init; }

    /// <summary>match: one confident answer (top candidate is tier A or B and clearly ahead). ambiguous: several plausible works, listed in order. none: nothing usable; see hint.</summary>
    public required MatchStatus Status { get; init; }

    /// <summary>Why this status was chosen.</summary>
    public required StatusReason StatusReason { get; init; }

    /// <summary>What to add to the query to narrow it (set for ambiguous and none).</summary>
    public required string? Hint { get; init; }

    /// <summary>True when the answer is lower quality than normal: the LLM was unavailable (heuristic parse, template sentences), a later Open Library call failed, or the deadline cut retrieval short.</summary>
    public required bool Degraded { get; init; }

    /// <summary>Empty unless degraded.</summary>
    public required IReadOnlyList<DegradedReason> DegradedReasons { get; init; }

    public required SearchInterpretation Interpretation { get; init; }

    /// <summary>At most limit works in final order. When the judge disagreed, its pick may take the last slot (second_opinion = true).</summary>
    public required IReadOnlyList<SearchCandidate> Candidates { get; init; }

    public required SearchMeta Meta { get; init; }
}

/// <summary>How the query was read. The hypothesis only steers retrieval: a book is returned only if Open Library has it.</summary>
public sealed record SearchInterpretation
{
    /// <summary>Title the LLM read from the text (null with the heuristic).</summary>
    public required string? Title { get; init; }
    public required string? Author { get; init; }
    public required IReadOnlyList<string> Characters { get; init; }

    /// <summary>Other meaningful words. With the heuristic: the cleaned search words.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>The English title when the text names a translated work.</summary>
    public required string? EnglishTitle { get; init; }

    /// <summary>The original-language title, when known.</summary>
    public required string? OriginalTitle { get; init; }

    /// <summary>Publication year taken from the query (only with at least two other words; a soft ±1 bonus, never a filter).</summary>
    public required int? Year { get; init; }

    /// <summary>Format and edition words removed from the search text (illustrated, deluxe…).</summary>
    public required IReadOnlyList<string> RemovedNoise { get; init; }

    /// <summary>llm, or heuristic when the LLM was off, failed or added nothing.</summary>
    public required InterpretationSource Source { get; init; }

    /// <summary>book: one particular book. topic: any books about a subject ("find me a book about bears"); the candidates are then books about it, never one match.</summary>
    public required QueryIntent Intent { get; init; }

    /// <summary>The subject terms searched for a topic request (from the LLM, or the "book about …" words without it); empty for a book request.</summary>
    public required IReadOnlyList<string> Topics { get; init; }
}

/// <summary>One Open Library work. title, author, first_publish_year and explanation are the brief's fields, byte for byte.</summary>
public sealed record SearchCandidate
{
    public required int Rank { get; init; }

    /// <summary>Open Library work id, e.g. "OL27482W".</summary>
    public required string WorkId { get; init; }
    public required string Title { get; init; }
    public required string? Subtitle { get; init; }

    /// <summary>Primary author(s) joined with ", " (at most three names, then "and N more"); null if Open Library lists none.</summary>
    public required string? Author { get; init; }

    /// <summary>Primary authors (at most three; the matched author is always kept).</summary>
    public required IReadOnlyList<AuthorLink> PrimaryAuthors { get; init; }

    /// <summary>How many primary authors the work has in total.</summary>
    public required int PrimaryAuthorsTotal { get; init; }

    /// <summary>Contributors with their role (adapter, illustrator…): lower-signal credits, at most three; the matched one is always kept.</summary>
    public required IReadOnlyList<ContributorLink> Contributors { get; init; }

    /// <summary>How many contributors the work has in total.</summary>
    public required int ContributorsTotal { get; init; }

    /// <summary>As listed by Open Library; null when absent or implausible. Some classics carry a wrong year upstream.</summary>
    public required int? FirstPublishYear { get; init; }
    public required int EditionCount { get; init; }
    /// <summary>The work page, opened on the edition the search matched when known (?edition=…); /books/{id} for an orphan edition record.</summary>
    public required string OpenLibraryUrl { get; init; }

    /// <summary>Medium cover image, or null when Open Library has none.</summary>
    public required string? CoverUrl { get; init; }

    /// <summary>The brief's hierarchy: a exact title + primary author · b exact title + contributor-only author · c near title + author · d author only · e no clear winner. Tiers label evidence; the score orders.</summary>
    public required MatchTier MatchTier { get; init; }
    public required string MatchTierLabel { get; init; }

    /// <summary>Topic requests: up to three of this work's Open Library subject headings that contain a topic word; empty otherwise.</summary>
    public required IReadOnlyList<string> MatchedSubjects { get; init; }

    /// <summary>One sentence citing concrete fields (grounded; a template when the LLM's sentence failed the check).</summary>
    public required string Explanation { get; init; }
    public required ExplanationSource ExplanationSource { get; init; }

    /// <summary>True only on the judge's pick when it disagreed with rank 1.</summary>
    public required bool SecondOpinion { get; init; }

    /// <summary>Duplicate Open Library records folded into this work.</summary>
    public required int DuplicatesFolded { get; init; }
}

public sealed record AuthorLink
{
    /// <summary>Open Library author id, e.g. "OL26320A", when known.</summary>
    public required string? Key { get; init; }
    public required string Name { get; init; }
    public required string? Url { get; init; }
}

public sealed record ContributorLink
{
    public required string? Key { get; init; }
    public required string Name { get; init; }

    /// <summary>adapter, illustrator, editor, translator… as Open Library lists it.</summary>
    public required string? Role { get; init; }
}

/// <summary>What this request actually did. Measured, never estimated, except est_cost_usd (a paid-tier estimate). Null = not known.</summary>
public sealed record SearchMeta
{
    /// <summary>Same value as the X-Request-Id header and an error's trace_id.</summary>
    public required string RequestId { get; init; }
    public required long ElapsedMs { get; init; }

    /// <summary>Per stage: clean, interpret, retrieve, rank, explain, judge.</summary>
    public required IReadOnlyDictionary<string, long> TimingsMs { get; init; }
    public required OpenLibraryMeta OpenLibrary { get; init; }
    public required LlmMeta Llm { get; init; }
    public required JudgeMeta Judge { get; init; }
}

public sealed record OpenLibraryMeta
{
    /// <summary>Logical search.json calls, cache hits included (so a warm and a cold run count the same calls).</summary>
    public required int Calls { get; init; }
    public required int CacheHits { get; init; }

    /// <summary>HTTP requests actually sent (retries and re-queries included; 0 for cache hits).</summary>
    public required int UpstreamRequests { get; init; }

    /// <summary>Requests repeated after a timeout, 429 or 5xx.</summary>
    public required int Retries { get; init; }

    /// <summary>Calls whose slow empty answer was asked once more (a slow 0-hit reply is treated as a cut-off, IN-064).</summary>
    public required int SlowEmptyRequeries { get; init; }

    /// <summary>The per-query call budget (Search:MaxCatalogCalls).</summary>
    public required int Budget { get; init; }
    public required IReadOnlyList<CatalogStep> Steps { get; init; }
}

public sealed record CatalogStep
{
    /// <summary>broad, short_title, hypothesis, original_title, person, person_any, any_of, person_fallback, author_works…</summary>
    public required string Step { get; init; }
    public required CatalogCallOutcome Outcome { get; init; }
    public required bool CacheHit { get; init; }
    public required int Attempts { get; init; }
    public required long ElapsedMs { get; init; }

    /// <summary>Works returned (null when the call failed).</summary>
    public required int? Results { get; init; }

    /// <summary>Open Library's numFound (null when the call failed).</summary>
    public required int? NumFound { get; init; }
}

public sealed record LlmMeta
{
    /// <summary>Provider(s) of the calls made; null when none were attempted.</summary>
    public required string? Provider { get; init; }
    public required string? Model { get; init; }

    /// <summary>Requests that reached the provider (cache hits and calls skipped for no key or budget excluded).</summary>
    public required int Calls { get; init; }
    public required int CacheHits { get; init; }

    /// <summary>Sum over provider requests; null if any of them didn't report usage.</summary>
    public required long? InputTokens { get; init; }
    public required long? OutputTokens { get; init; }

    /// <summary>Paid-tier equivalent in USD (even on a free tier); null when tokens or the model's price are unknown.</summary>
    public required decimal? EstCostUsd { get; init; }

    /// <summary>Operation → prompt id, e.g. interpret → interpret.v3.</summary>
    public required IReadOnlyDictionary<string, string> PromptVersions { get; init; }
    public required IReadOnlyList<LlmStep> Steps { get; init; }
}

public sealed record LlmStep
{
    /// <summary>interpret or explain.</summary>
    public required string Operation { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string PromptId { get; init; }
    public required LlmCallStatus Status { get; init; }
    public required bool CacheHit { get; init; }
    public required long ElapsedMs { get; init; }
    public required long? InputTokens { get; init; }
    public required long? OutputTokens { get; init; }
}

/// <summary>The optional second opinion (OpenAI Decisions). It can only gate status; it never reorders candidates.</summary>
public sealed record JudgeMeta
{
    public required bool Enabled { get; init; }

    /// <summary>off, skipped (not meaningful for this ranking), agreed, disagreed, chose_none, refused, unavailable.</summary>
    public required JudgeStatus Status { get; init; }

    /// <summary>The judge's chosen work id (null for none or no verdict).</summary>
    public required string? Pick { get; init; }

    /// <summary>Probability of the pick.</summary>
    public required double? Probability { get; init; }

    /// <summary>Agreement needs the same pick at or above this probability.</summary>
    public required double Threshold { get; init; }

    /// <summary>Null when there was no verdict.</summary>
    public required bool? Agreed { get; init; }
    public required string? Model { get; init; }
    public required long? LatencyMs { get; init; }
    public required long? InputTokens { get; init; }
    public required decimal? EstCostUsd { get; init; }

    /// <summary>Why it was skipped or unavailable (not_applicable, no_candidates, deadline…).</summary>
    public required string? Detail { get; init; }
}
