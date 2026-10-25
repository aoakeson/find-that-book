using System.Diagnostics;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Scoring;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindThatBook.Eval.Running;

public sealed record RunSettings
{
    public int Concurrency { get; init; } = 2;

    /// <summary>The API's default limit: what a user sees.</summary>
    public int ResultLimit { get; init; } = 5;

    /// <summary>Paid LLM calls (not cache hits; interpret + explain + judge) allowed in this run; null = unlimited.</summary>
    public int? MaxLlmCalls { get; init; } = 20;

    /// <summary>Open Library HTTP attempts allowed in this run; null = unlimited (the adapter still throttles to 3 req/s).</summary>
    public int? MaxCatalogRequests { get; init; }

    /// <summary>
    /// Stop the run when more than this share of searched rows (after at least 10) hit a provider error (timeout, 5xx,
    /// 429) on an interpret or explain call: an outage would otherwise be scored as model quality. Null = never stop.
    /// </summary>
    public double? MaxProviderErrorRate { get; init; }

    /// <summary>Pace interpret + explain calls to this rate; null = from AI:&lt;Provider&gt;:RequestsPerMinute (see <see cref="EvalRunner.PacedRate"/>); 0 = no pacing.</summary>
    public int? LlmCallsPerMinute { get; init; }

}

/// <param name="Outcome">The full pipeline outcome (null for rejected, errored or skipped rows); written to raw/ only.</param>
public sealed record RowOutcome(ResultRow Row, SearchOutcome? Outcome);

public sealed record RunResult(IReadOnlyList<RowOutcome> Rows, DateTimeOffset Started, TimeSpan Wall, string? StoppedReason);

/// <summary>
/// Runs a dataset through exactly what the API does for GET /api/books/search: <see cref="QueryValidator"/> first
/// (a rejection is the API's 400), then <see cref="SearchService.SearchAsync"/> in its own DI scope (one scope per
/// request, like ASP.NET Core), then scores the shown candidates. Rows come back in dataset order.
/// </summary>
public sealed class EvalRunner(EvalHost host, RunSettings settings)
{
    private readonly LlmPacer _pacer = new(settings.LlmCallsPerMinute ?? PacedRate(host));

    /// <summary>The interpret + explain calls-per-minute this run is paced to (0 = not paced).</summary>
    public int LlmPace => _pacer.CallsPerMinute;

    /// <summary>The API endpoint's grace above Search:RequestDeadline (SearchEndpoints.DeadlineGrace).</summary>
    public static readonly TimeSpan DeadlineGrace = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private int _llmUsed, _llmReserved, _olUsed, _searched, _providerErrorRows;
    private string? _stopped;

    public async Task<RunResult> RunAsync(Dataset dataset, Action<ResultRow>? progress = null, CancellationToken ct = default)
    {
        var started = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();
        var rows = new RowOutcome[dataset.Cases.Count];
        using var slots = new SemaphoreSlim(Math.Max(1, settings.Concurrency));
        var tasks = dataset.Cases.Select(async (c, i) =>
        {
            await slots.WaitAsync(ct);
            try
            {
                rows[i] = await RunCaseAsync(c, dataset.HeldOut, ct);
                progress?.Invoke(rows[i].Row);
            }
            finally
            {
                slots.Release();
            }
        });
        await Task.WhenAll(tasks);
        return new RunResult(rows, started, clock.Elapsed, _stopped);
    }

    /// <summary>
    /// The active provider's per-minute limit (AI:&lt;Provider&gt;:RequestsPerMinute via AiOptions.For(provider); Gemini free tier 15), paced to
    /// 90% because slots are claimed slightly before calls are sent. When the explain LLM is on, the API keeps the last
    /// AI:Budget:ExplainReserve permits of each minute for interpret calls, so the pace also stays under that line (fewer
    /// explain_reserve template fallbacks; the ones that still happen are counted, not treated as errors).
    /// </summary>
    public static int PacedRate(EvalHost host)
    {
        var o = host.Services.GetRequiredService<IOptions<AiOptions>>().Value;
        // The provider the app actually resolved ("auto" picks the first one with a key), not the requested name.
        var provider = host.Services.GetService<IAiAdmin>()?.GetStatus().ActiveProvider ?? host.Options.Provider;
        var rpm = o.For(provider.ToLowerInvariant())?.RequestsPerMinute ?? 0;
        if (rpm <= 0) return 0;
        var pace = Math.Max(1, rpm * 9 / 10);
        if (host.Options.ExplainsWithLlm) pace = Math.Min(pace, Math.Max(1, rpm - o.Budget.ExplainReserve - 1));
        return pace;
    }

    private int InterpretExplainPerQuery => (host.Options.InterpretsWithLlm ? 1 : 0) + (host.Options.ExplainsWithLlm ? 1 : 0);

    private int LlmCallsPerQuery => InterpretExplainPerQuery + (host.Options.Judge ? 1 : 0);

    public async Task<RowOutcome> RunCaseAsync(EvalCase c, bool heldOut, CancellationToken ct)
    {
        if (await ReserveAsync(ct) is { } skip) return new RowOutcome(Skipped(c, heldOut, skip), null);
        var probe = new QueryProbe(c.Id);
        var wall = Stopwatch.StartNew();
        RowMeta? settled = null;
        try
        {
            using var _ = EvalContext.Begin(probe);
            var validation = QueryValidator.Validate(c.Query);
            if (!validation.IsValid)
            {
                var observed = new Observed(Outcomes.Rejected, validation.ErrorCode, []);
                return new RowOutcome(Row(c, heldOut, observed, reason: validation.ErrorCode, error: validation.Message,
                    meta: new RowMeta { WallMs = wall.ElapsedMilliseconds }), null);
            }
            await _pacer.WaitAsync(InterpretExplainPerQuery, ct);
            wall.Restart(); // pacing waits are the eval's, not the API's

            // As the API endpoint: the pipeline's deadline plus a 2 s grace turns a hung adapter into search_timeout.
            await using var scope = host.Services.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<SearchOptions>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.RequestDeadline + DeadlineGrace);
            var search = scope.ServiceProvider.GetRequiredService<SearchService>();
            var outcome = await search.SearchAsync(validation.Query!, settings.ResultLimit, timeout.Token);
            var row = FromOutcome(c, heldOut, outcome, wall.ElapsedMilliseconds, probe, scope.ServiceProvider.GetService<IAiPricing>());
            settled = row.Meta;
            return new RowOutcome(row, outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var code = ErrorCode(ex);
            if (code == "not_implemented") lock (_gate) _stopped ??= $"pipeline not implemented yet ({ex.Message})";
            var meta = new RowMeta { WallMs = wall.ElapsedMilliseconds, Replay = ReplayOf(probe), Recorded = [.. probe.Recorded] };
            return new RowOutcome(Row(c, heldOut, new Observed(Outcomes.Error, code, []), reason: code, error: ex.Message, meta: meta), null);
        }
        finally
        {
            // One lock: release the reservation and book what was spent. A search that threw left no trace, so its
            // worst case is booked (never under-count paid calls).
            lock (_gate)
            {
                _llmReserved -= LlmCallsPerQuery;
                _llmUsed += settled is null ? LlmCallsPerQuery : PaidLlmCalls(settled);
                _olUsed += settled?.CatalogCalls.Sum(x => x.Attempts) ?? 0;
                if (settled is not null)
                {
                    _searched++;
                    if (HasProviderError(settled)) _providerErrorRows++;
                    if (settings.MaxProviderErrorRate is { } max && _searched >= 10 && (double)_providerErrorRows / _searched > max)
                        _stopped ??= $"provider errors on {_providerErrorRows} of {_searched} searched rows (> {max:P0}): stopped; " +
                                     "wait, then re-run the missing rows with --resume <this folder>";
                }
            }
        }
    }

    /// <summary>
    /// Claims this query's worst-case LLM calls before it starts, so concurrency can never overshoot the LLM budget.
    /// When only in-flight reservations are in the way it waits for them to settle; it stops the run only when the calls
    /// actually spent leave no room. The Open Library budget is soft: rows already running may finish past it.
    /// </summary>
    private async Task<string?> ReserveAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_stopped is not null) return _stopped;
                if (settings.MaxLlmCalls is { } max && _llmUsed + LlmCallsPerQuery > max)
                    return _stopped = $"LLM call budget reached ({max} paid calls; raise --max-llm-calls)";
                if (settings.MaxCatalogRequests is { } olMax && _olUsed >= olMax)
                    return _stopped = $"Open Library request budget reached ({olMax}; raise --max-ol-requests)";
                if (settings.MaxLlmCalls is not { } cap || _llmUsed + _llmReserved + LlmCallsPerQuery <= cap)
                {
                    _llmReserved += LlmCallsPerQuery;
                    return null;
                }
            }
            await Task.Delay(100, ct);
        }
    }

    /// <summary>A row the budget (or a missing pipeline) stopped before it ran: not scored, counted in the summary.</summary>
    private static ResultRow Skipped(EvalCase c, bool heldOut, string reason) =>
        new()
        {
            Id = c.Id, Query = c.Query, Category = c.Category, InSample = c.InSample, HeldOut = heldOut,
            Expected = ExpectedView.Of(c.Expected), Status = Outcomes.Skipped, Reason = reason,
            Pass = new PassView(c.Expected.Answerable, null, null, null, null, false, null), Meta = new RowMeta(),
        };

    /// <summary>
    /// An interpret or explain call failed on the provider's side (timeout, 5xx, 429), or was skipped during the cooldown
    /// a provider 429 started (budget_exhausted / "cooldown"): not our own per-minute or daily budget.
    /// </summary>
    public static bool HasProviderError(RowMeta meta) =>
        meta.LlmCalls.Any(l => l.Status is LlmCallStatus.Timeout or LlmCallStatus.ProviderError or LlmCallStatus.RateLimited
                               || (l.Status == LlmCallStatus.BudgetExhausted && l.Detail == "cooldown"));

    public static int PaidLlmCalls(RowMeta meta) =>
        meta.LlmCalls.Count(l => !l.CacheHit && l.Status is not (LlmCallStatus.NotConfigured or LlmCallStatus.BudgetExhausted))
        + (meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) } ? 1 : 0);

    private ResultRow FromOutcome(EvalCase c, bool heldOut, SearchOutcome o, long wallMs, QueryProbe probe, IAiPricing? pricing)
    {
        var items = o.Items.OrderBy(i => i.Rank).ToList();
        var shown = items.Select(i => new ShownView(i.Rank, i.Work.Work.WorkId, i.Work.Work.Title, i.Work.Work.AuthorNames,
            i.Work.Work.AuthorKeys, i.Work.Tier, Math.Round(i.Work.Score, 3), i.Work.Work.EditionCount, i.SecondOpinion,
            i.Explanation.Source, i.Work.FoldedWorkIds.Count > 0 ? i.Work.FoldedWorkIds : null,
#if FTB_PRE_TOPIC
            null, null)).ToList();
        var observed = new Observed(StatusName(o.Status), null,
            items.Select(i => new ObservedWork(i.Work.Work.WorkId, i.Work.Work.Title, i.Work.Work.AuthorNames, i.Work.Work.AuthorKeys,
                i.Work.FoldedWorkIds)).ToList(), Snake(o.Reason.ToString()));
#else
            SubjectsOf(i.Work) is { Count: > 0 } subjects ? [.. subjects.Take(12)] : null,
            i.Work.Evidence.SuggestedByInterpreter ? true : null)).ToList();
        var observed = new Observed(StatusName(o.Status), null,
            items.Select(i => new ObservedWork(i.Work.Work.WorkId, i.Work.Work.Title, i.Work.Work.AuthorNames, i.Work.Work.AuthorKeys,
                i.Work.FoldedWorkIds, SubjectsOf(i.Work))).ToList(), Snake(o.Reason.ToString()));
#endif

        var explainOk = o.Trace.LlmCalls.Any(l => l.Operation == "explain" && l.Succeeded);
        var baseItems = items.Where(i => !(i.SecondOpinion && i.Rank > settings.ResultLimit)).ToList(); // a late second opinion is always a template
        var (cost, unpriced) = Cost(o, pricing);
        var meta = new RowMeta
        {
            WallMs = wallMs,
            ElapsedMs = o.Trace.ElapsedMs,
            TimingsMs = o.Trace.TimingsMs,
            InterpretationSource = o.InterpretationSource,
            Hypothesis = o.Hypothesis,
            Degraded = o.Degraded,
            CatalogCalls = o.Trace.CatalogCalls,
            CatalogCallBudget = o.Trace.CatalogCallBudget,
            LlmCalls = o.Trace.LlmCalls,
            Judge = o.Judge,
            ExplanationsLlm = items.Count(i => i.Explanation.Source == ExplanationSource.Llm),
            ExplanationsTemplate = items.Count(i => i.Explanation.Source == ExplanationSource.Template),
            TemplateAfterLlmOk = explainOk ? baseItems.Count(i => i.Explanation.Source == ExplanationSource.Template) : 0,
            EstCostUsd = cost,
            UnpricedCalls = unpriced,
            Replay = ReplayOf(probe),
            Recorded = [.. probe.Recorded],
            HypothesisMiss = !probe.HypothesisMisses.IsEmpty,
            ExplainReserveFallbacks = o.Trace.LlmCalls.Count(l => l.Operation == "explain" && l.Detail == "explain_reserve"),
#if !FTB_PRE_TOPIC
            Topic = o.Topic,
#endif
        };
        return Row(c, heldOut, observed, o.Reason.ToString(), null, meta, shown, o.Hint);
    }

    private static (decimal? Cost, int Unpriced) Cost(SearchOutcome o, IAiPricing? pricing)
    {
        decimal? total = null;
        var unpriced = 0;
        // As the API's meta.llm: cache hits and 429s cost nothing; a sent call without usage is unpriced, never $0.
        foreach (var l in o.Trace.LlmCalls.Where(l => !l.CacheHit && l.Status is not (LlmCallStatus.NotConfigured or LlmCallStatus.BudgetExhausted or LlmCallStatus.RateLimited)))
        {
            if (l.InputTokens is not null && l.OutputTokens is not null && pricing?.Estimate(l.Model, l.InputTokens, l.OutputTokens) is { } usd)
                total = (total ?? 0) + usd;
            else unpriced++;
        }
        if (o.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped), Model: { } model, InputTokens: { } tokens })
        {
            // The judge reports input tokens only; the estimate covers what was reported.
            if (pricing?.Estimate(model, tokens, null) is { } usd) total = (total ?? 0) + usd;
            else unpriced++;
        }
        return (total, unpriced);
    }

    private static ReplayView? ReplayOf(QueryProbe probe) =>
        probe.ReplayHits.IsEmpty && probe.ReplayMisses.IsEmpty && probe.Unsupported.IsEmpty && probe.CacheFills.IsEmpty
            ? null
            : new ReplayView(probe.ReplayHits.Count, [.. probe.ReplayMisses], [.. probe.ReplayPartial], probe.CacheFills.Count, probe.Unsupported.Count);

    private static ResultRow Row(EvalCase c, bool heldOut, Observed observed, string? reason, string? error, RowMeta meta,
        IReadOnlyList<ShownView>? shown = null, string? hint = null)
    {
        var score = CaseScorer.Score(c.Expected, observed);
        return new ResultRow
        {
            Id = c.Id,
            Query = c.Query,
            Category = c.Category,
            InSample = c.InSample,
            HeldOut = heldOut,
            Expected = ExpectedView.Of(c.Expected),
            Status = observed.Outcome,
            Reason = reason is null ? null : Snake(reason),
            ErrorCode = observed.ErrorCode,
            Error = error,
            Hint = hint,
            Top5 = shown ?? [],
            Pass = PassView.Of(score),
            Meta = meta,
        };
    }

    // FTB_PRE_TOPIC: a build of this tool against the frozen pre-topic pipeline binaries (the rule ablations).
    /// <summary>
    /// Topic scoring reads the work's own subject list (filled for works from topic calls), independent of the
    /// matcher's MatchedSubjects; it falls back to MatchedSubjects when the work carries none.
    /// </summary>
#if !FTB_PRE_TOPIC
    public static IReadOnlyList<string> SubjectsOf(RankedWork w) => w.Work.Subjects.Count > 0 ? w.Work.Subjects : w.Evidence.MatchedSubjects;
#endif

    public static string StatusName(MatchStatus s) => s switch
    {
        MatchStatus.Match => Outcomes.Match,
        MatchStatus.Ambiguous => Outcomes.Ambiguous,
        _ => Outcomes.None,
    };

    private static string ErrorCode(Exception ex) => ex switch
    {
        NotImplementedException => "not_implemented",
        CatalogUnavailableException => "catalog_unavailable",
        CatalogTimeoutException => "catalog_timeout",
        CatalogBadResponseException => "catalog_bad_response",
        CatalogQueryRejectedException => "catalog_query_rejected",
        CatalogException => "catalog_error",
        SearchTimeoutException => "search_timeout",
        OperationCanceledException => "search_timeout",
        _ => "crash_" + Snake(ex.GetType().Name),
    };

    /// <summary>"ExactTitlePrimaryAuthor" → "exact_title_primary_author" (the API's enum spelling).</summary>
    public static string Snake(string pascal) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(pascal);
}
