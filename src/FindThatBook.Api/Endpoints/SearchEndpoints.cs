using System.Text.Json;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Errors;
using FindThatBook.Api.Hosting;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Search;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Endpoints;

/// <summary>The API's only view of the pipeline: a seam so HTTP tests can fake an outcome or an upstream failure.</summary>
internal interface IBookSearch
{
    Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken ct);
}

internal sealed class PipelineBookSearch(SearchService search) : IBookSearch
{
    public Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken ct) => search.SearchAsync(query, limit, ct);
}

internal static partial class SearchEndpoints
{
    /// <summary>
    /// A last-resort cap above the pipeline's own deadline, for an adapter that ignores cancellation. The pipeline
    /// itself returns what it has (degraded) at the deadline; this only turns a hang into a 504.
    /// </summary>
    internal static readonly TimeSpan DeadlineGrace = TimeSpan.FromSeconds(2);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/books/search", SearchAsync)
            .WithName("SearchBooks")
            .WithTags("Books")
            .WithSummary("Find the book a messy text most likely refers to")
            .WithDescription("""
                Interprets a messy query (title and/or author and/or keywords) with an LLM, searches Open Library,
                and returns up to `limit` ranked works, each with a one-sentence grounded explanation.
                AI interprets, code decides: ranking and status are deterministic code.

                **status**: `match` (one confident answer: top candidate is tier a or b and clearly ahead),
                `ambiguous` (several plausible works, in order; see `hint`), `none` (nothing usable; 200 with an
                empty list and a hint). **degraded** = the answer is lower quality than normal (LLM unavailable,
                a later Open Library call failed, or the deadline cut retrieval short); see `degraded_reasons`.
                The LLM never causes an error status.

                **Topic requests** ("find me a book about bears"): `interpretation.intent` is `topic`, the candidates are
                books about `interpretation.topics` (status `ambiguous`, reason `topic_search`, never `match`), and each
                lists the Open Library subjects that matched in `matched_subjects`.

                **match_tier** (the brief's hierarchy): `a` exact title + primary author · `b` exact title +
                contributor-only author · `c` near-match title + author · `d` author only (an author's most-published
                works) · `e` no clear winner. Tiers label the evidence; the score orders the list.

                Rate limits (defaults): 20 searches per minute per client, 60 per minute in total, 6 at once (16 more wait in line); 429 carries Retry-After.
                """)
            .Produces<SearchResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .RequireRateLimitBucket(RateLimitBucket.Search);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        HttpContext http,
        [FromQuery(Name = "q")] string? q,
        [FromQuery(Name = "limit")] string? limit,
        IBookSearch search,
        SearchResponseMapper mapper,
        SearchOptions options,
        ILoggerFactory loggers)
    {
        _ = limit; // bound for the OpenAPI document; parsed strictly from the raw query string below
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var shown = QueryParameters.ParseLimit(http.Request, "limit", "Limit", 1, options.MaxLimit, errors) ?? options.DefaultLimit;

        // The shared QueryValidator (DE-05, IN-001..IN-020); a repeated q is rejected before it can be joined into "a,b" (SY-13).
        string? query = null;
        string? queryCode = null;
        if (QueryParameters.IsRepeated(http.Request, "q"))
        {
            errors["Q"] = ["Give q only once."];
        }
        else
        {
            var validation = QueryValidator.Validate(q);
            query = validation.Query;
            if (!validation.IsValid)
            {
                errors["Q"] = [validation.Message ?? "The query is not valid."];
                queryCode = validation.ErrorCode;
            }
        }
        if (errors.Count > 0 || query is null)
        {
            // no_searchable_terms only when it is the sole problem; any malformed parameter makes it validation_failed.
            return errors.Count == 1 && queryCode == ApiProblems.NoSearchableTerms
                ? ApiProblems.Result(ApiProblems.Validation(errors, ApiProblems.NoSearchableTerms, QueryValidator.NoSearchableTermsHint))
                : ApiProblems.Result(ApiProblems.Validation(errors));
        }

        var log = loggers.CreateLogger("FindThatBook.Api.Search");
        var requestId = http.RequestId();
        LogQuery(log, requestId, query);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        deadline.CancelAfter(options.RequestDeadline + DeadlineGrace);
        var running = search.SearchAsync(query, shown, deadline.Token);
        SearchOutcome outcome;
        try
        {
            // WaitAsync bounds the await itself: a task that ignores its token can't hold the request (and its
            // concurrency permit) past the deadline. The token is still passed down for everything that cooperates.
            outcome = await running.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !http.RequestAborted.IsCancellationRequested)
        {
            Observe(running);
            throw new SearchTimeoutException(options.RequestDeadline);
        }
        catch (OperationCanceledException)
        {
            Observe(running);
            throw;
        }

        var response = mapper.Map(outcome, requestId);
        LogCompleted(log, response);
        return TypedResults.Ok(response);
    }

    private static void LogCompleted(ILogger log, SearchResponse r)
    {
        var m = r.Meta;
        LogCompleted(log, m.RequestId, Snake(r.Status), r.Candidates.Count > 0 ? Snake(r.Candidates[0].MatchTier) : "-",
            r.DegradedReasons.Count > 0 ? string.Join(",", r.DegradedReasons.Select(Snake)) : "-", m.ElapsedMs, r.Candidates.Count,
            m.Llm.Calls, m.Llm.CacheHits, m.Llm.InputTokens, m.Llm.OutputTokens, m.Llm.EstCostUsd,
            m.OpenLibrary.Calls, m.OpenLibrary.CacheHits, m.OpenLibrary.UpstreamRequests, Snake(m.Judge.Status));
    }

    /// <summary>An abandoned search may still fail later; observe it so the failure isn't an unobserved task exception.</summary>
    private static void Observe(Task task) => task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static string Snake<T>(T value) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    // One line per search with the numbers an operator needs. The query text is logged at Debug only (SY-33).
    [LoggerMessage(EventId = 100, EventName = "search.completed", Level = LogLevel.Information, Message =
        "search.completed {RequestId} status={Status} top_tier={TopTier} degraded={DegradedReasons} elapsed_ms={ElapsedMs} " +
        "candidates={Candidates} llm_calls={LlmCalls} llm_cache_hits={LlmCacheHits} input_tokens={InputTokens} " +
        "output_tokens={OutputTokens} est_cost_usd={EstCostUsd} ol_calls={OlCalls} ol_cache_hits={OlCacheHits} " +
        "ol_attempts={OlAttempts} judge={JudgeStatus}")]
    private static partial void LogCompleted(ILogger logger, string requestId, string status, string topTier, string degradedReasons,
        long elapsedMs, int candidates, int llmCalls, int llmCacheHits, long? inputTokens, long? outputTokens, decimal? estCostUsd,
        int olCalls, int olCacheHits, int olAttempts, string judgeStatus);

    [LoggerMessage(EventId = 101, EventName = "search.query", Level = LogLevel.Debug, Message = "search.query {RequestId} {Query}")]
    private static partial void LogQuery(ILogger logger, string requestId, string query);
}
