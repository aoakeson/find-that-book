using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Api.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Errors;

/// <summary>
/// Maps what escapes an endpoint to the catalog. Only Open Library failures with nothing in hand reach here
/// (the pipeline degrades instead whenever it has results); the LLM never produces an error status.
/// A client disconnect never gets here: the exception-handler middleware answers 499 itself without an error log (SY-29).
/// </summary>
internal sealed partial class ApiExceptionHandler(IHostEnvironment environment, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Retry-After for a 503 when Open Library didn't say (the circuit breaker's break duration).</summary>
    public static readonly TimeSpan DefaultCatalogRetryAfter = TimeSpan.FromSeconds(15);

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken cancellationToken)
    {
        TimeSpan? retryAfter = null;
        ProblemDetails problem;
        switch (exception)
        {
            case CatalogUnavailableException e:
                retryAfter = e.RetryAfter is { } ra && ra > TimeSpan.Zero ? ra : DefaultCatalogRetryAfter;
                problem = ApiProblems.Create(503, ApiProblems.CatalogUnavailable, "Open Library is unavailable",
                    $"Open Library did not answer successfully after {e.Attempts} attempt(s). Try again shortly.");
                break;
            case CatalogTimeoutException e:
                retryAfter = TimeSpan.FromSeconds(5);
                problem = ApiProblems.Create(504, ApiProblems.CatalogTimeout, "Open Library did not answer in time",
                    $"Every attempt timed out ({e.Attempts} attempt(s)). Try again shortly.");
                break;
            case SearchTimeoutException:
                retryAfter = TimeSpan.FromSeconds(5);
                problem = ApiProblems.Create(504, ApiProblems.SearchTimeout, "The search did not finish in time",
                    "No Open Library results arrived before the request deadline. Try again shortly.");
                break;
            case CatalogBadResponseException e:
                problem = ApiProblems.Create(502, ApiProblems.CatalogBadResponse, "Open Library returned an unexpected response",
                    $"The response could not be used ({e.Attempts} attempt(s)).");
                break;
            case BadHttpRequestException e:
                problem = ApiProblems.Create(e.StatusCode, ApiProblems.CodeForStatus(e.StatusCode), "The request could not be read",
                    environment.IsDevelopment() ? e.Message : null);
                break;
            default:
                problem = ApiProblems.Create(500, ApiProblems.InternalError, "Something went wrong on our side",
                    "The error was logged. Quote the trace_id if you report it.");
                if (environment.IsDevelopment())
                {
                    problem.Extensions["exception"] = exception.GetType().FullName;
                    problem.Extensions["message"] = exception.Message;
                    problem.Extensions["stack_trace"] = exception.StackTrace;
                }
                break;
        }

        if (exception is CatalogException catalog)
            LogUpstreamFailure(logger, ctx.RequestId(), problem.Status ?? 0, catalog.GetType().Name, catalog.Attempts);
        else if (problem.Status >= 500)
            LogUnhandled(logger, exception, ctx.RequestId());

        await ApiProblems.WriteAsync(ctx, problem, retryAfter);
        return true;
    }

    // The exception message is not logged for upstream failures: it can contain the request URL, i.e. the query.
    [LoggerMessage(EventId = 200, EventName = "search.failed", Level = LogLevel.Warning,
        Message = "search.failed {RequestId} status={Status} error={ErrorType} attempts={Attempts}")]
    private static partial void LogUpstreamFailure(ILogger logger, string requestId, int status, string errorType, int attempts);

    [LoggerMessage(EventId = 201, EventName = "request.unhandled", Level = LogLevel.Error, Message = "request.unhandled {RequestId}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string requestId);
}
