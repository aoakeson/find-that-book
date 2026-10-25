using FindThatBook.Api.Endpoints;
using FindThatBook.Api.Hosting;
using Microsoft.AspNetCore.Routing.Patterns;

namespace FindThatBook.Api.Errors;

/// <summary>
/// Everything under /api that no endpoint takes: a JSON 404 (or 405 for a known path with the wrong method),
/// never the SPA's index.html, so a frontend's <c>res.json()</c> always gets JSON (SY-16, SY-17).
/// </summary>
internal static class ApiFallback
{
    public static IEndpointConventionBuilder MapApiFallback(this IEndpointRouteBuilder app)
        => app.Map("/api/{**rest}", Handle).ExcludeFromDescription();

    private static IResult Handle(HttpContext ctx, EndpointDataSource endpoints)
    {
        var allowed = AllowedMethods(endpoints, ctx.Request.Path, includeLoopbackOnly: AdminEndpoints.IsLocalCaller(ctx));
        if (allowed.Count > 0)
        {
            ctx.Response.Headers.Allow = string.Join(", ", allowed);
            return ApiProblems.Result(ApiProblems.Create(405, ApiProblems.MethodNotAllowed, "Method not allowed",
                $"This endpoint accepts {string.Join(", ", allowed)}."));
        }
        return ApiProblems.Result(ApiProblems.Create(404, ApiProblems.NotFound, "No such API endpoint",
            "See /scalar or /openapi/v1.json for the endpoints."));
    }

    /// <summary>
    /// Methods of the mapped /api endpoints whose route template matches the path (parameters match any segment).
    /// Loopback-only endpoints count only for a local admin caller: anyone else must not learn they exist.
    /// </summary>
    private static List<string> AllowedMethods(EndpointDataSource endpoints, PathString path, bool includeLoopbackOnly)
    {
        var segments = (path.Value ?? "").Trim('/').Split('/');
        var methods = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern;
            if (pattern.Parameters.Any(p => p.IsCatchAll) || pattern.PathSegments.Count != segments.Length) continue;
            if (endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is not { } m) continue;
            if (!includeLoopbackOnly && endpoint.Metadata.GetMetadata<AdminEndpoints.LoopbackOnly>() is not null) continue;
            if (pattern.PathSegments.Select((s, i) => Matches(s, segments[i])).All(x => x))
                methods.UnionWith(m.HttpMethods);
        }
        return [.. methods];
    }

    private static bool Matches(RoutePatternPathSegment segment, string value)
    {
        if (!segment.IsSimple) return false;
        return segment.Parts[0] switch
        {
            RoutePatternLiteralPart literal => string.Equals(literal.Content, value, StringComparison.OrdinalIgnoreCase),
            RoutePatternParameterPart => value.Length > 0,
            _ => false,
        };
    }
}
