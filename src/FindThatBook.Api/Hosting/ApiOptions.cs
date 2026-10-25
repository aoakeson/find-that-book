using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Hosting;

/// <summary>Section "RateLimit". Fixed windows, no queueing (except the search concurrency gate).</summary>
public sealed class RateLimitOptions
{
    [Range(1, 3600)] public int WindowSeconds { get; set; } = 60;

    /// <summary>Searches per client IP per window.</summary>
    [Range(1, 100_000)] public int SearchPermitsPerIp { get; set; } = 20;

    /// <summary>Searches per window for everyone together: caps LLM/judge spend even if IPs rotate (SY-07, SY-08).</summary>
    [Range(1, 1_000_000)] public int SearchPermitsGlobal { get; set; } = 60;

    /// <summary>
    /// Searches running at once; more wait in the queue, beyond it 429 (SY-01). Open Library takes a burst of about
    /// 23 calls (3 sending + 20 queued) at 3 req/s, roughly 3 calls per cold search, so 6 leaves room for retries:
    /// a burst queues here at the front door instead of overflowing the Open Library throttle.
    /// </summary>
    [Range(1, 1000)] public int SearchConcurrency { get; set; } = 6;
    [Range(0, 10_000)] public int SearchQueue { get; set; } = 16;

    /// <summary>Autocomplete is local and cheap, so it gets its own, higher allowance.</summary>
    [Range(1, 1_000_000)] public int SuggestPermitsPerIp { get; set; } = 120;

    [Range(1, 100_000)] public int AdminPermitsPerIp { get; set; } = 30;
}

/// <summary>
/// Section "ForwardedHeaders". X-Forwarded-For is honoured only from these proxies. Empty (the default) means the
/// middleware is not used at all and the socket address is the client: we never clear the lists to "trust everyone".
/// </summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>Proxy IP addresses, e.g. "10.0.0.5".</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR form, e.g. "10.0.0.0/8".</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxy hops to unwrap; match the real hop count of the host (SY-07).</summary>
    [Range(1, 10)] public int ForwardLimit { get; set; } = 1;

    public bool Enabled => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

internal sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        var bad = options.KnownProxies.Where(p => !IPAddress.TryParse(p, out _))
            .Concat(options.KnownNetworks.Where(n => !IPNetwork.TryParse(n, out _)))
            .ToList();
        return bad.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"ForwardedHeaders: not an IP address or CIDR network: {string.Join(", ", bad)}");
    }
}

/// <summary>Section "Suggest": the autocomplete endpoint's bounds.</summary>
public sealed class SuggestOptions
{
    [Range(1, 50)] public int DefaultLimit { get; set; } = 8;
    [Range(1, 50)] public int MaxLimit { get; set; } = 10;
    [Range(1, 300)] public int MaxPrefixLength { get; set; } = 100;
}
