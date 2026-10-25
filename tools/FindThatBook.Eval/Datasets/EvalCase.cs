namespace FindThatBook.Eval.Datasets;

/// <summary>Observed outcome names. "rejected" = QueryValidator refused the query (the API's 400); "error" = the search threw.</summary>
public static class Outcomes
{
    public const string Match = "match";
    public const string Ambiguous = "ambiguous";
    public const string None = "none";
    public const string Rejected = "rejected";
    public const string Error = "error";

    /// <summary>Not run (budget reached or the pipeline is missing): never scored.</summary>
    public const string Skipped = "skipped";
}

/// <summary>What a case accepts. Which members are set decides how the row is scored (see <see cref="Scoring.CaseScorer"/>).</summary>
public sealed record Expectation
{
    /// <summary>Acceptable outcomes (match/ambiguous/none/rejected). Empty = status is not scored for this row.</summary>
    public IReadOnlyList<string> Statuses { get; init; } = [];

    /// <summary>Rejected rows: the validator's error code ("validation_failed", "no_searchable_terms").</summary>
    public string? ErrorCode { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>Work ids accepted at rank 1, and anywhere in the first five.</summary>
    public IReadOnlyList<string> Top1 { get; init; } = [];
    public IReadOnlyList<string> Top5 { get; init; } = [];

    /// <summary>Author rows (golden): a candidate counts if any of its author keys is listed.</summary>
    public IReadOnlyList<string> AuthorKeys { get; init; } = [];

    /// <summary>Author rows (golden "meyer"): a candidate counts if an author's last name token equals this.</summary>
    public string? AuthorSurname { get; init; }

    /// <summary>Author rows (held-out target_authors, jsonl author_only): a candidate counts if an author has the same NameKey.</summary>
    public IReadOnlyList<string> AuthorNames { get; init; } = [];

    /// <summary>Lenient sets (jsonl): a duplicate work with the same main-title key and this author also counts.</summary>
    public string? LenientTitle { get; init; }
    public string? LenientAuthor { get; init; }

    /// <summary>Battle rule (judge.py rerank): a candidate also counts when a duplicate folded into it is acceptable.</summary>
    public bool CountFolded { get; init; }

    /// <summary>Battle: the intended titles/authors, used only to tell a duplicate-record match from a wrong match.</summary>
    public IReadOnlyList<string> DupTitles { get; init; } = [];
    public IReadOnlyList<string> DupAuthors { get; init; } = [];

    /// <summary>The status reason the row requires as well as the status (topic rows: "topic_search").</summary>
    public string? ExpectedReason { get; init; }

    /// <summary>
    /// Topic rows: a shown work counts when one of its subjects matches a topic word (folded; whole word with an
    /// optional s/es plural, a prefix when the word ends with '*', phrases as phrases). Top-5 passes when at least
    /// <see cref="MinTopicHits"/> of the first five count; top-1 when the first one does.
    /// </summary>
    public IReadOnlyList<string> TopicWords { get; init; } = [];
    public int MinTopicHits { get; init; } = 3;
    public bool IsTopicRow => TopicWords.Count > 0;

    /// <summary>Held-out rule: a "match" whose top-1 is wrong is also a status error.</summary>
    public bool WrongMatchIsStatusError { get; init; }

    /// <summary>Display only: the expected title/author.</summary>
    public string? Describe { get; init; }

    public bool IsRejection => Statuses.Contains(Outcomes.Rejected);
    public bool IsAuthorRow => Top1.Count == 0 && LenientTitle is null && (AuthorKeys.Count > 0 || AuthorSurname is not null || AuthorNames.Count > 0);
    public bool Answerable => Top1.Count > 0 || Top5.Count > 0 || LenientTitle is not null || IsAuthorRow || IsTopicRow;
    public bool AllowsMatch => Statuses.Count == 0 || Statuses.Contains(Outcomes.Match);
}

/// <param name="InSample">The row was used while the research rules were tuned (golden, hard); false for held-out and generated sets.</param>
public sealed record EvalCase(string Id, string Query, string Category, Expectation Expected, bool InSample);

/// <param name="Label">Shown at the top of every summary (e.g. the synthetic-mirror caveat or the held-out banner).</param>
public sealed record Dataset(
    string Name,
    int Version,
    string Path,
    string Sha256,
    bool HeldOut,
    string Tuning,
    string? Label,
    IReadOnlyList<EvalCase> Cases)
{
    /// <summary>Short name for folder labels (defaults to the file name).</summary>
    public string Slug { get; init; } = System.IO.Path.GetFileNameWithoutExtension(Path).Replace('.', '-');

    public Dataset Take(int? limit) => limit is { } n && n < Cases.Count ? this with { Cases = Cases.Take(n).ToList() } : this;
}

/// <summary>Thrown when the sealed held-out set is requested without the explicit --heldout flag.</summary>
public sealed class HeldOutRefusedException(string path)
    : Exception($"{path} is the SEALED held-out set: never tune on it. Pass --heldout to run it for a final evaluation; its results are labelled HELD-OUT.");
