using FindThatBook.Api.Contracts;
using FindThatBook.Api.Errors;
using FindThatBook.Api.Hosting;
using FindThatBook.Core.Normalization;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Infrastructure.Suggest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Endpoints;

/// <summary>GET /api/suggest: a local index lookup per keystroke, cacheable by the browser.</summary>
internal static class SuggestEndpoints
{
    public static IEndpointRouteBuilder MapSuggestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/suggest", Suggest)
            .WithName("SuggestBooks")
            .WithTags("Suggest")
            .WithSummary("Autocomplete titles from a local index of real works")
            .WithDescription("""
                Prefix lookup over a prebuilt index of the most-published Open Library works: no Open Library or
                LLM call per keystroke. Match order: whole title starts with the prefix > a title word does > the
                author's surname does, then edition count. An empty prefix returns an empty list (a typing state,
                not an error). On select, run a normal search with the chosen title and author.
                Cached by clients for an hour; its own, higher rate limit (default 120 per minute per client).
                """)
            .Produces<SuggestResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimitBucket(RateLimitBucket.Suggest);
        return app;
    }

    private static IResult Suggest(
        HttpContext http,
        [FromQuery(Name = "prefix")] string? prefix,
        [FromQuery(Name = "limit")] string? limit,
        ISuggestIndex index,
        IOptions<SuggestOptions> settings,
        IOptions<OpenLibraryOptions> openLibrary)
    {
        var o = settings.Value;
        _ = (prefix, limit); // bound for the OpenAPI document; read strictly from the raw query string below
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var take = QueryParameters.ParseLimit(http.Request, "limit", "Limit", 1, o.MaxLimit, errors) ?? Math.Min(o.DefaultLimit, o.MaxLimit);
        var text = "";
        if (QueryParameters.IsRepeated(http.Request, "prefix")) errors["Prefix"] = ["Give prefix only once."];
        else
        {
            text = QueryValidator.Sanitize(http.Request.Query["prefix"].ToString()); // same cleaning as q (DE-05, IN-015, IN-019)
            if (text.Length > o.MaxPrefixLength) errors["Prefix"] = [$"The field Prefix must be at most {o.MaxPrefixLength} characters."];
        }
        if (errors.Count > 0) return ApiProblems.Result(ApiProblems.Validation(errors));

        var links = openLibrary.Value;
        var hits = text.Length == 0 ? [] : index.Lookup(text, take);
        http.Response.Headers.CacheControl = "public, max-age=3600";
        return TypedResults.Ok(new SuggestResponse
        {
            Prefix = text,
            Suggestions = [.. hits.Take(take).Select(s => new SuggestionItem
            {
                WorkId = s.WorkId,
                Title = s.Title,
                Author = s.Author,
                FirstPublishYear = s.FirstPublishYear is > 0 ? s.FirstPublishYear : null,
                CoverUrl = OpenLibraryLinks.Cover(links, s.CoverId),
                EditionCount = s.EditionCount,
                OpenLibraryUrl = OpenLibraryLinks.Work(links, s.WorkId),
                Match = s.Match,
            })],
        });
    }
}
