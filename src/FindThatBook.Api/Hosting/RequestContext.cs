using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FindThatBook.Api.Hosting;

/// <summary>
/// Per-request identity: one id from browser to log (X-Request-Id = meta.request_id = ProblemDetails trace_id),
/// and the socket peer as seen before any forwarded header was applied (the admin loopback check needs it).
/// </summary>
internal static class RequestContext
{
    public const string RequestIdHeader = "X-Request-Id";
    private const string RequestIdKey = "ftb.request_id";
    private const string SocketPeerKey = "ftb.socket_peer";

    /// <summary>First middleware: fixes the id for the whole request and stamps it on every response, errors included.</summary>
    public static IApplicationBuilder UseRequestContext(this IApplicationBuilder app) => app.Use((ctx, next) =>
    {
        var id = Activity.Current?.Id ?? ctx.TraceIdentifier;
        ctx.Items[RequestIdKey] = id;
        ctx.Items[SocketPeerKey] = ctx.Connection.RemoteIpAddress;
        // OnStarting survives the exception handler's Response.Clear(), so 500s carry the header too.
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[RequestIdHeader] = id;
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        });
        return next(ctx);
    });

    public static string RequestId(this HttpContext ctx)
        => ctx.Items.TryGetValue(RequestIdKey, out var id) && id is string s ? s : Activity.Current?.Id ?? ctx.TraceIdentifier;

    /// <summary>The TCP peer, before forwarded headers. Null in hosts that don't report one.</summary>
    public static IPAddress? SocketPeer(this HttpContext ctx)
        => ctx.Items.TryGetValue(SocketPeerKey, out var ip) ? ip as IPAddress : ctx.Connection.RemoteIpAddress;

    /// <summary>
    /// True only for a direct local call: the socket peer is loopback, no forwarded header changed the client
    /// address, and the request carries no proxy headers at all (a proxy on this machine may be relaying a remote user).
    /// </summary>
    public static bool IsDirectLoopback(this HttpContext ctx)
    {
        var peer = ctx.SocketPeer();
        if (peer is null || !IPAddress.IsLoopback(peer)) return false;
        if (!Equals(peer, ctx.Connection.RemoteIpAddress)) return false;
        var headers = ctx.Request.Headers;
        return !headers.ContainsKey("X-Forwarded-For") && !headers.ContainsKey("X-Forwarded-Host")
            && !headers.ContainsKey("X-Forwarded-Proto") && !headers.ContainsKey("Forwarded")
            && !headers.ContainsKey("X-Real-IP") && !headers.ContainsKey("X-Original-For");
    }

    /// <summary>
    /// A browser sent this on behalf of another site. A bodiless POST is a CORS "simple request": any page the
    /// developer visits could fire it at localhost (unreadable, but the call happens). Browsers mark such requests
    /// with Sec-Fetch-Site and Origin; curl and Scalar send neither or their own origin.
    /// </summary>
    public static bool IsCrossSiteBrowserRequest(this HttpContext ctx)
    {
        var headers = ctx.Request.Headers;
        var site = headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site is not ("same-origin" or "none")) return true;
        var origin = headers.Origin.ToString();
        return origin.Length > 0
            && !string.Equals(origin, $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rate-limit partition key: the client address, IPv4-mapped addresses unwrapped, IPv6 grouped by /64.</summary>
    public static string ClientKey(this HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // One host usually owns a whole /64; keying on the full address would let it rotate freely.
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }
        return ip.ToString();
    }
}
