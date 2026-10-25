using FindThatBook.Core.Explanations;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Search;

/// <summary>Everything one search produced. The API maps it to the HTTP contract; the eval scores it.</summary>
public sealed record SearchOutcome
{
    public required CleanedQuery Query { get; init; }
    public QueryHypothesis? Hypothesis { get; init; }
    public required InterpretationSource InterpretationSource { get; init; }
    public required MatchStatus Status { get; init; }
    public required StatusReason Reason { get; init; }
    public string? Hint { get; init; }
    public required IReadOnlyList<SearchItem> Items { get; init; }
    public required JudgeVerdict Judge { get; init; }
    public required IReadOnlyList<DegradedReason> Degraded { get; init; }
    public required SearchTrace Trace { get; init; }

    /// <summary>Set when the request was treated as "books about a subject" (the terms actually searched).</summary>
    public TopicRequest? Topic { get; init; }

    /// <summary>The full ranking before the limit was applied (for the eval and debugging).</summary>
    public required RankingResult Ranking { get; init; }
}

public sealed record SearchItem(int Rank, RankedWork Work, Explanation Explanation, bool SecondOpinion);

public enum CatalogCallOutcome { Ok, Failed, Rejected, Deadline }

/// <param name="Request">The parameters sent (user words included); never logged above Debug.</param>
public sealed record CatalogCallRecord(
    string Step,
    string Request,
    CatalogCallOutcome Outcome,
    bool CacheHit,
    int Attempts,
    long ElapsedMs,
    int? Results,
    int? NumFound,
    bool SlowEmptyRetried,
    string? Error);

/// <summary>Measured, never estimated: what this request actually did.</summary>
public sealed record SearchTrace(
    long ElapsedMs,
    IReadOnlyDictionary<string, long> TimingsMs,
    IReadOnlyList<CatalogCallRecord> CatalogCalls,
    IReadOnlyList<LlmCall> LlmCalls,
    int CatalogCallBudget);
