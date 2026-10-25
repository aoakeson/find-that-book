using System.Reflection;
using FindThatBook.Api.Contracts;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Api.Endpoints;

internal static partial class HealthEndpoints
{
    private static readonly string Version = (typeof(HealthEndpoints).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", Health)
            .WithName("Health")
            .WithTags("System")
            .WithSummary("Liveness and configuration summary")
            .WithDescription("Never calls Open Library or an LLM (a probe must not burn quota). Always 200 while the process runs.")
            .Produces<HealthResponse>(StatusCodes.Status200OK);
        return app;
    }

    // Everything optional: health must answer even if a component is missing or broken.
    private static HealthResponse Health(IServiceProvider services, IHostEnvironment environment, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("FindThatBook.Api.Health");
        var ai = Try(logger, "ai", () => services.GetService<IAiAdmin>()?.GetStatus());
        var judge = Try(logger, "judge", () => services.GetService<IMatchJudge>() is { } j ? j.IsEnabled : (bool?)null);
        var index = Try(logger, "suggest", () => services.GetService<ISuggestIndex>() is { } s
            ? new SuggestIndexInfo { Version = s.Version, Count = s.Count }
            : null);
        var upstream = Try(logger, "upstream", () => services.GetService<IUpstreamHealthMonitor>()?.GetStatus());
        return new HealthResponse
        {
            Status = "ok",
            Version = Version,
            Environment = environment.EnvironmentName,
            AiProvider = ai?.ActiveProvider,
            AiConfigured = ai?.AiConfigured,
            JudgeEnabled = judge,
            SuggestIndex = index,
            Upstream = upstream is null ? null : new UpstreamHealth
            {
                OpenLibraryCircuit = upstream.OpenLibraryCircuit,
                OpenLibraryLastFailure = upstream.OpenLibraryLastFailure,
                OpenLibraryLastFailureReason = upstream.OpenLibraryLastFailureReason,
                LlmStatus = upstream.LlmStatus,
                LlmLastFailure = upstream.LlmLastFailure,
                LlmLastFailureReason = upstream.LlmLastFailureReason,
            },
        };
    }

    private static T? Try<T>(ILogger logger, string component, Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogComponentFailed(logger, component, ex.GetType().Name);
            return default;
        }
    }

    [LoggerMessage(EventId = 400, EventName = "health.component_failed", Level = LogLevel.Warning,
        Message = "health.component_failed component={Component} error={ErrorType}")]
    private static partial void LogComponentFailed(ILogger logger, string component, string errorType);
}
