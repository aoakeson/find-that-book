using System.Threading.RateLimiting;
using FindThatBook.Api.Errors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Hosting;

/// <summary>Which allowance an endpoint draws from. Attached with <c>.RequireRateLimitBucket(...)</c>.</summary>
internal sealed record RateLimitBucket(string Name)
{
    public static readonly RateLimitBucket Search = new("search");
    public static readonly RateLimitBucket Suggest = new("suggest");
    public static readonly RateLimitBucket Admin = new("admin");
}

/// <summary>
/// Search: per-IP window, then one global window (so spoofed or rotating IPs can't drain the LLM budget, SY-07),
/// then a concurrency gate (SY-01). Order matters: a client already over its own limit is rejected before it can
/// spend a global permit, so one noisy client can't lock everyone out. Suggest and admin have per-IP windows only.
/// </summary>
internal static partial class RateLimiting
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((o, limits) =>
        {
            var l = limits.Value;
            var window = TimeSpan.FromSeconds(l.WindowSeconds);
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = OnRejectedAsync;
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => Bucket(ctx) switch
                {
                    "search" => Window("search|" + ctx.ClientKey(), l.SearchPermitsPerIp, window),
                    "suggest" => Window("suggest|" + ctx.ClientKey(), l.SuggestPermitsPerIp, window),
                    "admin" => Window("admin|" + ctx.ClientKey(), l.AdminPermitsPerIp, window),
                    _ => RateLimitPartition.GetNoLimiter("none"),
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => Bucket(ctx) == "search"
                    ? Window("search|global", l.SearchPermitsGlobal, window)
                    : RateLimitPartition.GetNoLimiter("none")),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => Bucket(ctx) == "search"
                    ? RateLimitPartition.GetConcurrencyLimiter("search|concurrency", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = l.SearchConcurrency,
                        QueueLimit = l.SearchQueue,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    })
                    : RateLimitPartition.GetNoLimiter("none")));
        });
        return services;
    }

    public static TBuilder RequireRateLimitBucket<TBuilder>(this TBuilder builder, RateLimitBucket bucket)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(bucket);

    private static string? Bucket(HttpContext ctx) => ctx.GetEndpoint()?.Metadata.GetMetadata<RateLimitBucket>()?.Name;

    private static RateLimitPartition<string> Window(string key, int permits, TimeSpan window)
        => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var ctx = context.HttpContext;
        // A full window reports its wait; the concurrency gate doesn't, and frees up within seconds.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(1);
        var seconds = ApiProblems.RetryAfterSeconds(retryAfter);
        var bucket = Bucket(ctx) ?? "none";
        LogRejected(ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("FindThatBook.Api.RateLimiting"),
            ctx.RequestId(), bucket, seconds);
        var problem = ApiProblems.Create(429, ApiProblems.RateLimited, "Too many requests",
            $"The {bucket} limit was reached. Try again in {seconds} s.");
        await ApiProblems.WriteAsync(ctx, problem, TimeSpan.FromSeconds(seconds));
    }

    [LoggerMessage(EventId = 300, EventName = "rate_limited", Level = LogLevel.Warning,
        Message = "rate_limited {RequestId} bucket={Bucket} retry_after_s={RetryAfterSeconds}")]
    private static partial void LogRejected(ILogger logger, string requestId, string bucket, int retryAfterSeconds);
}
