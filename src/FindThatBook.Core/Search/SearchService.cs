using FindThatBook.Core.Explanations;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;

namespace FindThatBook.Core.Search;

/// <summary>
/// The pipeline: clean → interpret ∥ broad search → hypothesis calls → fallback ladder → rank →
/// explain ∥ judge → status. AI interprets, code decides: the LLM only steers retrieval and writes
/// sentences; ranking and status are deterministic code. Per-request state lives in <see cref="SearchRun"/>,
/// so this class holds none.
/// </summary>
public sealed class SearchService
{
    private readonly InterpretationService _interpretation;
    private readonly IBookCatalog _catalog;
    private readonly ExplanationService _explanations;
    private readonly IMatchJudge _judge;
    private readonly SearchOptions _options;
    private readonly TimeProvider _time;

    public SearchService(InterpretationService interpretation, IBookCatalog catalog, ExplanationService explanations,
        IMatchJudge judge, SearchOptions options, TimeProvider time)
    {
        _interpretation = interpretation;
        _catalog = catalog;
        _explanations = explanations;
        _judge = judge;
        _options = options;
        _time = time;
    }

    /// <exception cref="CatalogException">Open Library failed and no candidates were retrieved.</exception>
    /// <exception cref="SearchTimeoutException">The deadline passed before any candidate was retrieved.</exception>
    public Task<SearchOutcome> SearchAsync(string query, int? limit, CancellationToken requestAborted)
    {
        var shown = Math.Clamp(limit ?? _options.DefaultLimit, 1, _options.MaxLimit);
        return new SearchRun(this, shown, requestAborted).RunAsync(query);
    }

    /// <summary>
    /// One request. Threading rule: only the orchestrating flow (RunAsync and what it awaits in sequence) touches the
    /// pool, the step table and the call count. Work that runs in parallel (catalog calls, explain, judge) only appends
    /// to the logs, timings and degraded list, which go through <see cref="_gate"/>.
    /// </summary>
    private sealed class SearchRun(SearchService svc, int limit, CancellationToken requestAborted)
    {
        private readonly long _started = svc._time.GetTimestamp();
        private readonly Lock _gate = new();
        private readonly Dictionary<string, long> _timings = [];
        private readonly List<CatalogCallRecord> _catalogCalls = [];
        private readonly List<LlmCall> _llmCalls = [];
        private readonly List<DegradedReason> _degraded = [];
        private readonly Dictionary<string, int?> _steps = [];
        private readonly Dictionary<string, int> _claimOrder = [];
        private readonly CandidatePool _pool = new();
        private int _callsIssued;
        private bool _anyFailed;
        private bool _anyCatalogSucceeded;
        private bool _retrievalDeadline;
        private CatalogException? _lastFailure;

        private SearchOptions Options => svc._options;

        public async Task<SearchOutcome> RunAsync(string raw)
        {
            var query = Time("clean", () => QueryCleaner.Clean(raw));
            if (query.IsEmpty)
            {
                // The API rejects this case with 400 no_searchable_terms (DE-05); the eval and direct callers get "none".
                return Finish(query, null, InterpretationSource.Heuristic, RankingResult.Empty,
                    new StatusDecision(MatchStatus.None, StatusReason.NoSearchableTerms, "Add a title or author."), [],
                    svc._judge.IsEnabled ? JudgeVerdict.Skipped("no_searchable_terms") : JudgeVerdict.Off);
            }

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            overall.CancelAfter(Options.RequestDeadline);
            using var retrieval = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
            retrieval.CancelAfter(Options.RequestDeadline - Options.PostRetrievalReserve);

            // Stage 1: the LLM parse runs beside call 1, so it hides behind Open Library's latency.
            var retrieveStarted = svc._time.GetTimestamp();
            var broadRequest = CatalogQueries.Broad(query);
            TryClaim(broadRequest);
            var broadTask = ExecuteAsync(broadRequest, retrieval.Token);
            var interpreted = await TimeAsync("interpret", () => InterpretAsync(query, overall.Token));
            Record(interpreted.Call, interpreted.Degraded);
            var hypothesis = interpreted.Hypothesis;

            var early = RetrievalLadder.AfterInterpretation(query, hypothesis).Where(TryClaim)
                .Select(r => ExecuteAsync(r, retrieval.Token)).ToList();
            var broad = await broadTask;
            if (broad.Outcome == CatalogCallOutcome.Rejected && CatalogQueries.AfterRejected(query) is { } replan && TryClaim(replan))
            {
                Merge(broad); // records the rejected step; the title= re-plan replaces it as stage 1's result (DE-05)
                broad = await ExecuteAsync(replan, retrieval.Token);
            }
            var earlyResults = await Task.WhenAll(early);

            // Merge in step order, not completion order: the pool (and so the ranking) must not depend on timing.
            Merge(broad);
            foreach (var r in earlyResults) Merge(r);
            // Only an outage is an error. A query Open Library refused even as title= (after the DE-05 re-plan) is
            // valid input with no searchable result: it falls through to "none".
            if (_pool.Count == 0 && broad.Outcome == CatalogCallOutcome.Failed && broad.Error is { } broadError
                && earlyResults.All(r => r.Outcome != CatalogCallOutcome.Ok))
                throw broadError;
            if (_pool.Count == 0 && (broad.Outcome == CatalogCallOutcome.Deadline || earlyResults.Any(r => r.Outcome == CatalogCallOutcome.Deadline)))
                throw new SearchTimeoutException(Options.RequestDeadline);

            // Stage 2: calls that needed call 1's pool (a focused retry when nothing in it is the hypothesis title).
            var late = RetrievalLadder.AfterBroad(query, hypothesis, _pool.Works, _steps.Keys).Where(TryClaim)
                .Select(r => ExecuteAsync(r, retrieval.Token)).ToList();
            foreach (var r in await Task.WhenAll(late)) Merge(r);

            // Stage 3: the fallback ladder, one call at a time, each decided on the current ranking.
            var ranking = Rank(query, hypothesis);
            while (!retrieval.IsCancellationRequested && _callsIssued < Options.MaxCatalogCalls)
            {
                var next = RetrievalLadder.Next(new LadderState(query, hypothesis, _pool.Works, ranking,
                    new Dictionary<string, int?>(_steps), Options.MaxCatalogCalls - _callsIssued));
                if (next is null || !TryClaim(next)) break;
                var result = await ExecuteAsync(next, retrieval.Token);
                Merge(result);
                if (result.Outcome is CatalogCallOutcome.Failed or CatalogCallOutcome.Deadline) break; // keep what we have
                ranking = Rank(query, hypothesis);
            }
            if (_anyFailed && _anyCatalogSucceeded) Degrade(DegradedReason.CatalogPartial);
            if (retrieval.IsCancellationRequested && !requestAborted.IsCancellationRequested) Degrade(DegradedReason.DeadlineReached);
            lock (_gate) _timings["retrieve"] = ElapsedMs(retrieveStarted);

            var source = hypothesis is null ? InterpretationSource.Heuristic : InterpretationSource.Llm;
            if (ranking.Ranked.Count == 0 && !_anyCatalogSucceeded && _lastFailure is not null)
                throw _lastFailure; // nothing retrieved and Open Library failed: an outage, not "no such book"
            if (ranking.Ranked.Count == 0 && (retrieval.IsCancellationRequested || _retrievalDeadline))
                throw new SearchTimeoutException(Options.RequestDeadline);
            if (ranking.Ranked.Count == 0)
            {
                var noJudge = svc._judge.IsEnabled ? JudgeVerdict.Skipped("no_candidates") : JudgeVerdict.Off;
                return Finish(query, hypothesis, source, ranking,
                    StatusPolicy.Decide(ranking, noJudge, Options.JudgeThreshold), [], noJudge);
            }

            if (ranking.Status == MatchStatus.None)
            {
                // The contract: "none" is an answer with no candidates. Nothing to explain or judge.
                var notJudged = svc._judge.IsEnabled ? JudgeVerdict.Skipped("status_none") : JudgeVerdict.Off;
                return Finish(query, hypothesis, source, ranking,
                    StatusPolicy.Decide(ranking, notJudged, Options.JudgeThreshold), [], notJudged);
            }

            // Stage 4: explanations and the judge in parallel. The judge only gates status.
            var baseShown = ranking.Ranked.Take(limit).ToList();
            var explainTask = TimeAsync("explain", () => ExplainAsync(baseShown, query, hypothesis, overall.Token));
            var judgeTask = TimeAsync("judge", () => JudgeAsync(query, ranking, overall.Token));
            await Task.WhenAll(explainTask, judgeTask);
            var batch = explainTask.Result;
            var verdict = judgeTask.Result;
            Record(batch.Call, batch.Degraded);

            var decision = StatusPolicy.Decide(ranking, verdict, Options.JudgeThreshold);
            var shown = StatusPolicy.Shown(ranking, decision, limit);
            var items = shown.Select((w, i) => new SearchItem(
                    i + 1,
                    w,
                    batch.ByWorkId.TryGetValue(w.Work.WorkId, out var e)
                        ? e
                        : svc._explanations.Templates([w], query, hypothesis).ByWorkId[w.Work.WorkId],
                    w.Work.WorkId == decision.SecondOpinionWorkId))
                .ToList();
            return Finish(query, hypothesis, source, ranking, decision, items, verdict) with { GroundingRejections = batch.Rejected };
        }

        private async Task<InterpretationOutcome> InterpretAsync(CleanedQuery query, CancellationToken ct)
        {
            try
            {
                return await svc._interpretation.InterpretAsync(query, ct);
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
            {
                // Our own deadline, not the caller: carry on without a hypothesis (the LLM-off arm).
                return new InterpretationOutcome(null, InterpretationSource.Heuristic, DegradedReason.DeadlineReached, null);
            }
        }

        private RankingResult Rank(CleanedQuery query, QueryHypothesis? hypothesis)
        {
            var started = svc._time.GetTimestamp();
            var ranking = MatchRanker.Rank(query, hypothesis, _pool.Works, Options.Ranker);
            lock (_gate) _timings["rank"] = _timings.GetValueOrDefault("rank") + ElapsedMs(started);
            return ranking;
        }

        private async Task<ExplanationBatch> ExplainAsync(IReadOnlyList<RankedWork> works, CleanedQuery query, QueryHypothesis? hypothesis, CancellationToken ct)
        {
            try
            {
                return await svc._explanations.ExplainAsync(works, query, hypothesis, ct);
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
            {
                Degrade(DegradedReason.DeadlineReached);
                return svc._explanations.Templates(works, query, hypothesis);
            }
        }

        private async Task<JudgeVerdict> JudgeAsync(CleanedQuery query, RankingResult ranking, CancellationToken ct)
        {
            if (!svc._judge.IsEnabled) return JudgeVerdict.Off;
            if (!StatusPolicy.ShouldJudge(ranking)) return JudgeVerdict.Skipped("not_applicable");
            try
            {
                return await svc._judge.JudgeAsync(StatusPolicy.BuildJudgeRequest(query.Raw, ranking, Options.JudgeMaxCandidates), ct);
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
            {
                return new JudgeVerdict(JudgeStatus.Unavailable, Detail: "deadline");
            }
        }

        private async Task<StepResult> ExecuteAsync(CatalogRequest request, CancellationToken ct)
        {
            var started = svc._time.GetTimestamp();
            try
            {
                var response = await svc._catalog.SearchAsync(request.Query, ct);
                Log(request, CatalogCallOutcome.Ok, response.Stats.CacheHit, response.Stats.Attempts, started,
                    response.Works.Count, response.NumFound, response.Stats.SlowEmptyRetried, null);
                return new StepResult(request, CatalogCallOutcome.Ok, response.Works, null);
            }
            catch (CatalogQueryRejectedException ex)
            {
                Log(request, CatalogCallOutcome.Rejected, false, ex.Attempts, started, null, null, false, ex.Message);
                return new StepResult(request, CatalogCallOutcome.Rejected, [], ex);
            }
            catch (CatalogException ex)
            {
                Log(request, CatalogCallOutcome.Failed, false, ex.Attempts, started, null, null, false, ex.GetType().Name);
                return new StepResult(request, CatalogCallOutcome.Failed, [], ex);
            }
            catch (OperationCanceledException ex) when (!requestAborted.IsCancellationRequested)
            {
                // Our deadline cut the call off; report the HTTP requests it had already sent.
                var attempts = ex is CatalogCancelledException cancelled ? cancelled.Attempts : 0;
                Log(request, CatalogCallOutcome.Deadline, false, attempts, started, null, null, false, "deadline");
                return new StepResult(request, CatalogCallOutcome.Deadline, [], null);
            }
        }

        private void Merge(StepResult result)
        {
            _steps[result.Request.Step] = result.Outcome == CatalogCallOutcome.Ok ? result.Works.Count : null;
            if (result.Outcome == CatalogCallOutcome.Ok)
            {
                _anyCatalogSucceeded = true;
                _pool.Add(result.Works);
            }
            else if (result.Outcome == CatalogCallOutcome.Failed)
            {
                _anyFailed = true;
                _lastFailure = result.Error;
            }
            else if (result.Outcome == CatalogCallOutcome.Deadline)
            {
                _retrievalDeadline = true;
                Degrade(DegradedReason.DeadlineReached);
            }
        }

        /// <summary>Takes one call from the budget for a step not issued yet. The step's result count is filled in by Merge.</summary>
        private bool TryClaim(CatalogRequest request)
        {
            if (_callsIssued >= Options.MaxCatalogCalls || _steps.ContainsKey(request.Step)) return false;
            _callsIssued++;
            _steps[request.Step] = null;
            _claimOrder[request.Step] = _claimOrder.Count;
            return true;
        }

        private void Log(CatalogRequest request, CatalogCallOutcome outcome, bool cacheHit, int attempts, long started,
            int? results, int? numFound, bool slowEmpty, string? error)
        {
            var record = new CatalogCallRecord(request.Step, request.Query.ToString(), outcome, cacheHit, attempts,
                ElapsedMs(started), results, numFound, slowEmpty, error);
            lock (_gate) _catalogCalls.Add(record);
        }

        private void Record(LlmCall? call, DegradedReason? degraded)
        {
            lock (_gate)
            {
                if (call is not null) _llmCalls.Add(call);
            }
            if (degraded is { } d) Degrade(d);
        }

        private void Degrade(DegradedReason reason)
        {
            lock (_gate)
            {
                if (!_degraded.Contains(reason)) _degraded.Add(reason);
            }
        }

        private T Time<T>(string stage, Func<T> work)
        {
            var started = svc._time.GetTimestamp();
            var result = work();
            lock (_gate) _timings[stage] = ElapsedMs(started);
            return result;
        }

        private async Task<T> TimeAsync<T>(string stage, Func<Task<T>> work)
        {
            var started = svc._time.GetTimestamp();
            var result = await work();
            lock (_gate) _timings[stage] = ElapsedMs(started);
            return result;
        }

        private long ElapsedMs(long started) => (long)svc._time.GetElapsedTime(started).TotalMilliseconds;

        private SearchOutcome Finish(CleanedQuery query, QueryHypothesis? hypothesis, InterpretationSource source,
            RankingResult ranking, StatusDecision decision, IReadOnlyList<SearchItem> items, JudgeVerdict verdict)
        {
            lock (_gate)
            {
                return new SearchOutcome
                {
                    Query = query,
                    Hypothesis = hypothesis,
                    InterpretationSource = source,
                    Status = decision.Status,
                    Reason = decision.Reason,
                    Hint = decision.Hint,
                    Items = items,
                    Judge = verdict,
                    Degraded = [.. _degraded.Order()],
                    // Report a topic only when the ranker treated it as one: a typed exact title ("novel on yellow paper")
                    // stays a book search even though its words look like a topic frame.
                    Topic = TopicRequest.From(query, hypothesis) is { } topic && (ranking.Ranked.Count == 0 || ranking.TopicLabel is not null)
                        ? topic
                        : null,
                    Ranking = ranking,
                    // Calls run in parallel; report them in the order they were planned so identical inputs give identical meta.
                    Trace = new SearchTrace(ElapsedMs(_started), new Dictionary<string, long>(_timings),
                        [.. _catalogCalls.OrderBy(c => _claimOrder.GetValueOrDefault(c.Step, int.MaxValue))], [.. _llmCalls],
                        Options.MaxCatalogCalls),
                };
            }
        }
    }

    private sealed record StepResult(CatalogRequest Request, CatalogCallOutcome Outcome, IReadOnlyList<CandidateWork> Works, CatalogException? Error);
}

/// <summary>The request deadline passed before any candidate was retrieved. Maps to 504 search_timeout.</summary>
public sealed class SearchTimeoutException(TimeSpan deadline)
    : Exception($"No Open Library results within the {deadline.TotalSeconds:0} s deadline.");
