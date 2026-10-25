using System.Text.RegularExpressions;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Errors;
using FindThatBook.Api.Hosting;
using FindThatBook.Infrastructure.Ai;

namespace FindThatBook.Api.Endpoints;

/// <summary>
/// Runtime AI settings for the future frontend's settings panel. Mapped only in Development, and only answers a
/// direct loopback caller (everything else gets the same 404 as an unknown route). Changes are in memory and lost
/// on restart; .env and appsettings are never rewritten. Keys go in, never come out, and are never logged.
/// </summary>
internal static partial class AdminEndpoints
{
    private static readonly string[] ChatProviders = ["gemini", "anthropic", "openai"];

    /// <summary>The judge can have its own key (it falls back to the OpenAI key when it has none).</summary>
    private static readonly string[] KeyNames = [.. ChatProviders, "judge"];

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Not in the OpenAPI document (it is public); docs/API.md describes these for the frontend.
        var group = app.MapGroup("/api/admin/ai")
            .ExcludeFromDescription()
            .WithMetadata(LoopbackOnly.Instance)
            .RequireRateLimitBucket(RateLimitBucket.Admin);

        group.MapGet("", (IAiAdmin admin) => TypedResults.Ok(AdminAiStatus.From(admin.GetStatus())));
        group.MapPut("", UpdateSettings);
        group.MapPut("/keys/{provider}", SetKey);
        group.MapDelete("/keys/{provider}", ClearKey);
        group.MapPost("/probe/{provider}", ProbeAsync);
        return app;
    }

    /// <summary>
    /// Runs right after routing, before rate limiting, body binding and validation, so a remote or cross-site caller
    /// can't tell the endpoint exists (a malformed body would otherwise answer 400 instead of 404).
    /// </summary>
    public static IApplicationBuilder UseLoopbackOnlyEndpoints(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<LoopbackOnly>() is null || IsLocalCaller(http))
        {
            await next(http);
            return;
        }
        LogRefused(Logger(http), http.RequestId());
        await ApiProblems.WriteAsync(http, ApiProblems.Create(404, ApiProblems.NotFound, "No such API endpoint",
            "See /scalar or /openapi/v1.json for the endpoints."));
    });

    /// <summary>A direct loopback call that no other website triggered: the only caller the admin API answers.</summary>
    internal static bool IsLocalCaller(HttpContext http) => http.IsDirectLoopback() && !http.IsCrossSiteBrowserRequest();

    internal sealed class LoopbackOnly
    {
        public static readonly LoopbackOnly Instance = new();
    }

    private static IResult UpdateSettings(AdminAiUpdate update, IAiAdmin admin, HttpContext http)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (provider, model) in update.Models ?? new Dictionary<string, string>())
        {
            if (!IsName(provider)) errors["Models"] = ["Model keys must be provider names (gemini, anthropic, openai…)."];
            else if (string.IsNullOrWhiteSpace(model) || model.Length > 100 || !ModelId().IsMatch(model))
                errors[$"Models.{provider}"] = ["A model id is 1-100 characters of letters, digits and . _ : / -"];
        }
        if (errors.Count > 0) return ApiProblems.Result(ApiProblems.Validation(errors));
        try
        {
            var status = admin.Update(new AiSettingsUpdate(update.Provider, update.Models, update.JudgeEnabled));
            LogChanged(Logger(http), http.RequestId(), "settings", update.Provider ?? "-");
            return TypedResults.Ok(AdminAiStatus.From(status));
        }
        catch (ArgumentException ex)
        {
            return ApiProblems.Result(ApiProblems.Validation(new Dictionary<string, string[]> { [ex.ParamName ?? "Update"] = [ex.Message] }));
        }
    }

    private static IResult SetKey(string provider, AdminSetKey body, IAiAdmin admin, HttpContext http)
    {
        if (ProviderError(provider, KeyNames) is { } bad) return bad;
        var key = body.ApiKey!.Trim();
        if (key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return ApiProblems.Result(ApiProblems.Validation(new Dictionary<string, string[]> { ["ApiKey"] = ["A key has no spaces or control characters."] }));
        try
        {
            var status = admin.SetKey(provider, key);
            LogChanged(Logger(http), http.RequestId(), "key_set", provider);
            return TypedResults.Ok(AdminAiStatus.From(status));
        }
        catch (ArgumentException)
        {
            // Never echo the exception: a provider's message could quote what it was given.
            return ApiProblems.Result(ApiProblems.Validation(new Dictionary<string, string[]> { ["ApiKey"] = ["The key was not accepted."] }));
        }
    }

    private static IResult ClearKey(string provider, IAiAdmin admin, HttpContext http)
    {
        if (ProviderError(provider, KeyNames) is { } bad) return bad;
        var status = admin.ClearKey(provider);
        LogChanged(Logger(http), http.RequestId(), "key_cleared", provider);
        return TypedResults.Ok(AdminAiStatus.From(status));
    }

    private static async Task<IResult> ProbeAsync(string provider, IAiAdmin admin, HttpContext http)
    {
        if (ProviderError(provider, ChatProviders) is { } bad) return bad;
        var result = AdminProbeResult.From(await admin.ProbeAsync(provider, http.RequestAborted));
        return TypedResults.Ok(result with { Detail = result.Detail is null ? null : SecretRedaction.Redact(result.Detail) });
    }

    private static IResult? ProviderError(string provider, string[] allowed) => allowed.Contains(provider, StringComparer.Ordinal)
        ? null
        : ApiProblems.Result(ApiProblems.Validation(new Dictionary<string, string[]>
        {
            ["Provider"] = [$"Provider must be one of: {string.Join(", ", allowed)}."],
        }));

    private static bool IsName(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiLetterLower);

    private static ILogger Logger(HttpContext http) => http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("FindThatBook.Api.Admin");

    [GeneratedRegex(@"^[A-Za-z0-9._:/\-]+$")]
    private static partial Regex ModelId();

    // Only what changed and for which provider: never the key, not even its fingerprint.
    [LoggerMessage(EventId = 500, EventName = "admin.ai.changed", Level = LogLevel.Information,
        Message = "admin.ai.changed {RequestId} change={Change} provider={Provider}")]
    private static partial void LogChanged(ILogger logger, string requestId, string change, string provider);

    [LoggerMessage(EventId = 501, EventName = "admin.refused", Level = LogLevel.Warning,
        Message = "admin.refused {RequestId}: not a direct loopback call")]
    private static partial void LogRefused(ILogger logger, string requestId);
}
