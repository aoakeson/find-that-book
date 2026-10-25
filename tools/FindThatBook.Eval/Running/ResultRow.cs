using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Eval.Running;

/// <summary>One line of results.jsonl: the query, what was expected, what came back, the pass flags and the measured meta.</summary>
public sealed record ResultRow
{
    public required string Id { get; init; }
    public required string Query { get; init; }
    public required string Category { get; init; }
    public bool InSample { get; init; }
    public bool HeldOut { get; init; }
    public required ExpectedView Expected { get; init; }

    /// <summary>match | ambiguous | none | rejected | error | skipped (<see cref="Outcomes"/>).</summary>
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
    public string? Hint { get; init; }
    /// <summary>The shown candidates in order (the limit, plus the judge's second-opinion pick when it sat lower); scoring reads the first five.</summary>
    public IReadOnlyList<ShownView> Top5 { get; init; } = [];
    public required PassView Pass { get; init; }
    public required RowMeta Meta { get; init; }

    public Observed ToObserved() => new(Status, ErrorCode,
        Top5.Select(s => new ObservedWork(s.WorkId, s.Title, s.Authors, s.AuthorKeys, s.FoldedWorkIds, s.Subjects)).ToList(), Reason);
}

public sealed record ExpectedView(
    IReadOnlyList<string> Status,
    IReadOnlyList<string> Top1,
    IReadOnlyList<string> Top5,
    IReadOnlyList<string> AuthorKeys,
    string? AuthorSurname,
    IReadOnlyList<string> AuthorNames,
    string? ErrorCode,
    string? Describe)
{
    public static ExpectedView Of(Expectation e) =>
        new(e.Statuses, e.Top1, e.Top5, e.AuthorKeys, e.AuthorSurname, e.AuthorNames, e.ErrorCode, e.Describe);
}

public sealed record ShownView(
    int Rank,
    string WorkId,
    string Title,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> AuthorKeys,
    MatchTier Tier,
    double Score,
    int EditionCount,
    bool SecondOpinion,
    ExplanationSource ExplanationSource,
    IReadOnlyList<string>? FoldedWorkIds = null,
    IReadOnlyList<string>? Subjects = null,
    bool? SuggestedByInterpreter = null);

public sealed record PassView(bool Answerable, bool? Top1, bool? Top5, bool? Status, bool? MatchCorrect, bool FalseMatch, int? TargetRank,
    bool DupRecordMatch = false)
{
    public static PassView Of(CaseScore s) => new(s.Answerable, s.Top1, s.Top5, s.Status, s.MatchCorrect, s.FalseMatch, s.TargetRank, s.DupRecordMatch);

    public CaseScore ToScore(string status) =>
        new(Answerable, Top1, Top5, Status, status == Outcomes.Match, MatchCorrect, FalseMatch, TargetRank, DupRecordMatch);
}

/// <summary>Measured per query. Null = not measured (never estimated).</summary>
public sealed record RowMeta
{
    public long WallMs { get; init; }
    public long? ElapsedMs { get; init; }
    public IReadOnlyDictionary<string, long> TimingsMs { get; init; } = new Dictionary<string, long>();
    public InterpretationSource? InterpretationSource { get; init; }
    public QueryHypothesis? Hypothesis { get; init; }
    public IReadOnlyList<DegradedReason> Degraded { get; init; } = [];
    public IReadOnlyList<CatalogCallRecord> CatalogCalls { get; init; } = [];
    public int? CatalogCallBudget { get; init; }
    public IReadOnlyList<LlmCall> LlmCalls { get; init; } = [];
    public JudgeVerdict? Judge { get; init; }
    public int ExplanationsLlm { get; init; }
    public int ExplanationsTemplate { get; init; }

    /// <summary>Template sentences shown although the explain call succeeded: the grounding check rejected the LLM sentence (or it was missing).</summary>
    public int TemplateAfterLlmOk { get; init; }

    /// <summary>Estimated cost of this query's paid LLM calls (interpret, explain, judge); null when nothing was priced.</summary>
    public decimal? EstCostUsd { get; init; }

    /// <summary>LLM calls (incl. judge) that were not cache hits and had no price.</summary>
    public int UnpricedCalls { get; init; }
    public ReplayView? Replay { get; init; }
    public IReadOnlyList<string> Recorded { get; init; } = [];

    /// <summary>Hypothesis replay had no entry for this query: the row ran LLM-off.</summary>
    public bool HypothesisMiss { get; init; }

    /// <summary>Explain calls the API's budget refused to keep permits for interpret (detail explain_reserve): template shown.</summary>
    public int ExplainReserveFallbacks { get; init; }

#if !FTB_PRE_TOPIC
    /// <summary>The pipeline's topic-request decision (terms, author hint, interpreter suggestions), when it made one.</summary>
    public TopicRequest? Topic { get; init; }
#endif
}

/// <param name="Hits">Requests answered from a recording (cache fills included).</param>
/// <param name="Fills">Requests the record-on-miss cache fetched from the backend and saved.</param>
/// <param name="Unsupported">Requests the backend cannot answer faithfully (person: on the mirror), answered empty.</param>
public sealed record ReplayView(int Hits, IReadOnlyList<string> Misses, IReadOnlyList<string> Partial, int Fills = 0, int Unsupported = 0);

public static class EvalJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static JsonSerializerOptions Indented { get; } = new(Options) { WriteIndented = true };
}
