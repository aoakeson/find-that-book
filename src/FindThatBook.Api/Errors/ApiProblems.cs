using System.Globalization;
using System.Text.Json;
using FindThatBook.Api.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Errors;

/// <summary>
/// The error catalog (RFC 9457). Every error body is application/problem+json with a stable <c>code</c> and the
/// request's <c>trace_id</c> (= X-Request-Id = meta.request_id). We write it ourselves rather than through content
/// negotiation, so an <c>Accept: application/xml</c> client still gets the same JSON (SY-10, SY-19).
/// </summary>
internal static class ApiProblems
{
    public const string ContentType = "application/problem+json";

    public const string ValidationFailed = "validation_failed";
    public const string NoSearchableTerms = "no_searchable_terms";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string RateLimited = "rate_limited";
    public const string CatalogBadResponse = "catalog_bad_response";
    public const string CatalogUnavailable = "catalog_unavailable";
    public const string CatalogTimeout = "catalog_timeout";
    public const string SearchTimeout = "search_timeout";
    public const string InternalError = "internal_error";

    /// <summary>Relative problem type URI, documented in docs/API.md: "/problems/catalog-unavailable".</summary>
    public static string TypeFor(string code) => "/problems/" + code.Replace('_', '-');

    public static string CodeForStatus(int status) => status switch
    {
        400 => ValidationFailed,
        404 => NotFound,
        405 => MethodNotAllowed,
        429 => RateLimited,
        502 => CatalogBadResponse,
        503 => CatalogUnavailable,
        504 => SearchTimeout,
        >= 500 => InternalError,
        _ => "http_" + status.ToString(CultureInfo.InvariantCulture),
    };

    public static ProblemDetails Create(int status, string code, string title, string? detail = null) => new()
    {
        Status = status,
        Type = TypeFor(code),
        Title = title,
        Detail = detail,
        Extensions = { ["code"] = code },
    };

    /// <summary>400 with field errors keyed by C# member name ("Q", "Limit": the documented .NET convention).</summary>
    public static HttpValidationProblemDetails Validation(IDictionary<string, string[]> errors, string code = ValidationFailed, string? hint = null)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Type = TypeFor(code),
            Title = code == NoSearchableTerms ? "The query has no searchable words." : "One or more validation errors occurred.",
            Extensions = { ["code"] = code },
        };
        if (hint is not null) problem.Extensions["hint"] = hint;
        return problem;
    }

    /// <summary>Adds the fields every problem carries. Also used for framework-generated problems via CustomizeProblemDetails.</summary>
    public static void Enrich(HttpContext ctx, ProblemDetails problem)
    {
        problem.Status ??= ctx.Response.StatusCode;
        var code = problem.Extensions.TryGetValue("code", out var c) && c is string s ? s : CodeForStatus(problem.Status.Value);
        problem.Extensions["code"] = code;
        problem.Type = TypeFor(code);
        problem.Extensions.Remove("traceId");
        problem.Extensions["trace_id"] = ctx.RequestId();
    }

    public static IResult Result(ProblemDetails problem, TimeSpan? retryAfter = null) => new ProblemResult(problem, retryAfter);

    public static async Task WriteAsync(HttpContext ctx, ProblemDetails problem, TimeSpan? retryAfter = null)
    {
        Enrich(ctx, problem);
        ctx.Response.StatusCode = problem.Status!.Value;
        if (retryAfter is { } wait) ctx.Response.Headers.RetryAfter = RetryAfterSeconds(wait).ToString(CultureInfo.InvariantCulture);
        ctx.Response.ContentType = ContentType;
        var json = ctx.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        await JsonSerializer.SerializeAsync(ctx.Response.Body, problem, problem.GetType(), json, ctx.RequestAborted);
    }

    /// <summary>Whole seconds, rounded up, at least 1 (a 0 would invite an immediate retry).</summary>
    public static int RetryAfterSeconds(TimeSpan wait) => (int)Math.Clamp(Math.Ceiling(wait.TotalSeconds), 1, 86_400);

    private sealed class ProblemResult(ProblemDetails problem, TimeSpan? retryAfter) : IResult, IStatusCodeHttpResult
    {
        public int? StatusCode => problem.Status;

        public Task ExecuteAsync(HttpContext httpContext) => WriteAsync(httpContext, problem, retryAfter);
    }
}
