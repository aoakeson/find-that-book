namespace FindThatBook.Core.Model;

/// <summary>What the search claims about its top result.</summary>
public enum MatchStatus { Match, Ambiguous, None }

/// <summary>The brief's matching hierarchy (4a–4e). Tiers label evidence; the score orders candidates.</summary>
public enum MatchTier
{
    /// <summary>Exact/normalized title + primary author (or an exact title with no author named).</summary>
    A,
    /// <summary>Exact/normalized title + contributor-only author.</summary>
    B,
    /// <summary>Near-match title + author match.</summary>
    C,
    /// <summary>Author-only: the author matched, the title did not.</summary>
    D,
    /// <summary>No clear winner: partial title, keywords or character evidence only.</summary>
    E,
}

/// <summary>Why the status was chosen. Serialized as snake_case strings.</summary>
public enum StatusReason
{
    ExactTitlePrimaryAuthor,
    ExactTitleContributorAuthor,
    ExactTitle,
    NearTitleAuthor,
    AuthorOnly,
    CloseScores,
    ScoreConflict,
    UnsupportedGuess,
    WeakEvidence,
    JudgeAgreed,
    /// <summary>The judge picked the same work but below the threshold (not a disagreement).</summary>
    JudgeLowConfidence,
    JudgeDisagreed,
    JudgeChoseNone,
    NoCandidates,
    NoSearchableTerms,
    /// <summary>A topic request ("a book about bears"): several fitting books, never one claimed match.</summary>
    TopicSearch,
}

/// <summary>Why the answer is lower quality than normal. Judge trouble is never "degraded".</summary>
public enum DegradedReason
{
    AiNotConfigured,
    AiBudgetExhausted,
    InterpretationFallback,
    ExplanationFallback,
    CatalogPartial,
    DeadlineReached,
}

public enum InterpretationSource { Llm, Heuristic }

/// <summary>One particular book (title, author, characters or its plot) vs any books about a subject, genre or setting.</summary>
public enum QueryIntent { Book, Topic }

public enum ExplanationSource { Llm, Template }

public enum JudgeStatus { Off, Skipped, Agreed, Disagreed, ChoseNone, Refused, Unavailable }

/// <summary>Words that ask for a different work than the canonical one. Edition words (illustrated, deluxe…) are not intents.</summary>
public enum EditionIntent { Adaptation, StudyGuide, Omnibus }

public enum AuthorRole { Primary, Contributor }
