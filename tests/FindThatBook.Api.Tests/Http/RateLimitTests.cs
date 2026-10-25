using System.Net;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Search;

namespace FindThatBook.Api.Tests.Http;

/// <summary>
/// SY-07/SY-08/SY-10/SY-01: per-client and global search windows, a separate suggest allowance, a concurrency gate,
/// and X-Forwarded-For honoured only from configured proxies. Windows are an hour long so nothing refills mid-test.
/// </summary>
public sealed class RateLimitTests
{
    private static ApiFactory Api(int perIp = 1000, int global = 1000, int suggest = 1000, string? knownProxy = null,
        int concurrency = 1000, int queue = 0, FakeBookSearch? search = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimit:WindowSeconds"] = "3600",
            ["RateLimit:SearchPermitsPerIp"] = perIp.ToString(),
            ["RateLimit:SearchPermitsGlobal"] = global.ToString(),
            ["RateLimit:SuggestPermitsPerIp"] = suggest.ToString(),
            ["RateLimit:SearchConcurrency"] = concurrency.ToString(),
            ["RateLimit:SearchQueue"] = queue.ToString(),
        };
        if (knownProxy is not null) settings["ForwardedHeaders:KnownProxies:0"] = knownProxy;
        return new ApiFactory { Search = search ?? new FakeBookSearch(), Settings = settings };
    }

    private static Task<HttpResponseMessage> Search(HttpClient client, string socketIp, string? forwardedFor = null, string accept = "application/json")
    {
        var headers = new List<(string, string)> { ("Accept", accept) };
        if (forwardedFor is not null) headers.Add(("X-Forwarded-For", forwardedFor));
        return client.SendAsync(Wire.Get("/api/books/search?q=hobbit", socketIp, [.. headers]), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SY10_OverThePerClientLimit_Is429ProblemJson_WithRetryAfter()
    {
        await using var api = Api(perIp: 2);
        var client = api.Client();
        (await Search(client, "198.51.100.7")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "198.51.100.7")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejected = await Search(client, "198.51.100.7", accept: "application/xml");
        var problem = await rejected.ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBe(3600);
        problem["detail"]!.GetValue<string>().ShouldContain("search");
        api.Logs.Named("rate_limited").ShouldHaveSingleItem();

        (await Search(client, "198.51.100.8")).StatusCode.ShouldBe(HttpStatusCode.OK); // another client is unaffected
    }

    [Fact]
    public async Task SY07_ForgedXForwardedFor_DoesNotBuyANewBucket()
    {
        await using var api = Api(perIp: 2);
        var client = api.Client();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) statuses.Add((await Search(client, "203.0.113.9", forwardedFor: $"10.1.1.{i}")).StatusCode);

        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task SY07_RotatingClients_StillHitTheGlobalLimit()
    {
        await using var api = Api(perIp: 100, global: 3);
        var client = api.Client();
        for (var i = 1; i <= 3; i++) (await Search(client, $"192.0.2.{i}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await Search(client, "192.0.2.4")).ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task ATrustedProxy_SplitsClientsByForwardedFor_ButTheGlobalLimitStillCaps()
    {
        await using var api = Api(perIp: 1, global: 3, knownProxy: "10.0.0.1");
        var client = api.Client();

        (await Search(client, "10.0.0.1", forwardedFor: "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "10.0.0.1", forwardedFor: "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Search(client, "10.0.0.1", forwardedFor: "198.51.100.2")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "10.0.0.1", forwardedFor: "198.51.100.3")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "10.0.0.1", forwardedFor: "198.51.100.4")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task XForwardedFor_FromAnUnknownPeer_IsIgnoredEvenWhenAProxyIsConfigured()
    {
        await using var api = Api(perIp: 1, knownProxy: "10.0.0.1");
        var client = api.Client();
        (await Search(client, "203.0.113.50", forwardedFor: "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "203.0.113.50", forwardedFor: "198.51.100.2")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AClientOverItsOwnLimit_DoesNotSpendTheGlobalAllowance()
    {
        await using var api = Api(perIp: 1, global: 3);
        var client = api.Client();
        for (var i = 0; i < 6; i++) await Search(client, "198.51.100.66"); // 1 allowed, 5 refused per client

        (await Search(client, "198.51.100.67")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "198.51.100.68")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ipv6Clients_AreGroupedBySlash64()
    {
        await using var api = Api(perIp: 1);
        var client = api.Client();
        (await Search(client, "2001:db8:1:2::1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Search(client, "2001:db8:1:2::ffff")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Search(client, "2001:db8:1:3::1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suggest_HasItsOwnAllowance()
    {
        await using var api = Api(perIp: 1, suggest: 2);
        var client = api.Client();
        await Search(client, "198.51.100.20");
        (await Search(client, "198.51.100.20")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        Task<HttpResponseMessage> Suggest() => client.SendAsync(Wire.Get("/api/suggest?prefix=har", "198.51.100.20"), TestContext.Current.CancellationToken);
        (await Suggest()).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Suggest()).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await Suggest()).ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task Health_IsNeverRateLimited()
    {
        await using var api = Api(perIp: 1, global: 1, suggest: 1);
        var client = api.Client();
        for (var i = 0; i < 5; i++)
            (await client.SendAsync(Wire.Get("/api/health", "198.51.100.30"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SY01_BeyondTheConcurrencyGateAndQueue_SearchesAreShedWith429()
    {
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var search = new FakeBookSearch
        {
            Handler = async (q, _, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                return Outcomes.None(q);
            },
        };
        await using var api = Api(concurrency: 1, queue: 0, search: search);
        var client = api.Client();

        var first = Search(client, "198.51.100.40");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var shed = await Search(client, "198.51.100.41");
        await shed.ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
        shed.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBe(1);

        release.SetResult();
        (await first).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
