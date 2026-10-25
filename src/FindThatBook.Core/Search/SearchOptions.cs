namespace FindThatBook.Core.Search;

/// <summary>Pipeline limits. Bound from configuration section "Search" by the composition root.</summary>
public sealed record SearchOptions
{
    public int DefaultLimit { get; init; } = 5;
    public int MaxLimit { get; init; } = 10;

    /// <summary>
    /// Logical search.json requests per user query, cache hits included (so the plan is the same warm or cold).
    /// HTTP retries inside a call are attempts, bounded separately by the catalog adapter and reported per call.
    /// </summary>
    public int MaxCatalogCalls { get; init; } = 5;

    /// <summary>The whole request. Past it, results already in hand are returned (degraded) rather than discarded.</summary>
    public TimeSpan RequestDeadline { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>Retrieval stops this long before the deadline, so ranking, explanations and the judge still fit.</summary>
    public TimeSpan PostRetrievalReserve { get; init; } = TimeSpan.FromSeconds(5);

    public double JudgeThreshold { get; init; } = 0.9;
    public int JudgeMaxCandidates { get; init; } = 8;

    /// <summary>Ranking rule switches. Not configuration: only the eval's ablation arms change it (internal).</summary>
    internal Matching.RankerOptions Ranker { get; init; } = Matching.RankerOptions.Default;

    /// <summary>Fails at startup on a nonsensical configuration (SY-36), not on the first request.</summary>
    public SearchOptions Validated()
    {
        var errors = new List<string>();
        if (DefaultLimit < 1 || DefaultLimit > MaxLimit) errors.Add("DefaultLimit must be between 1 and MaxLimit.");
        if (MaxLimit is < 1 or > 10) errors.Add("MaxLimit must be between 1 and 10.");
        if (MaxCatalogCalls is < 1 or > 10) errors.Add("MaxCatalogCalls must be between 1 and 10.");
        if (RequestDeadline <= TimeSpan.Zero || PostRetrievalReserve < TimeSpan.Zero || PostRetrievalReserve >= RequestDeadline)
            errors.Add("RequestDeadline must be positive and larger than PostRetrievalReserve.");
        if (JudgeThreshold is <= 0 or > 1) errors.Add("JudgeThreshold must be in (0, 1].");
        if (JudgeMaxCandidates is < 2 or > 20) errors.Add("JudgeMaxCandidates must be between 2 and 20.");
        return errors.Count == 0 ? this : throw new InvalidOperationException("Invalid Search options: " + string.Join(" ", errors));
    }
}
