using System.Net;
using System.Net.Http.Json;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Infrastructure.Ai;

namespace FindThatBook.Api.Tests.Http;

/// <summary>
/// The Development-only, loopback-only AI settings API. Anything else sees the same 404 as an unknown route.
/// Keys are write-only: never in a response body, never in a log line.
/// </summary>
public sealed class AdminTests
{
    private const string Key = "AIzaTESTKEY-0123456789abcdefghijklmnop";
    private const string KeyCore = "TESTKEY-0123456789"; // any slice of the key must not appear either

    private static ApiFactory Dev(bool realAdmin = false) => new() { EnvironmentName = "Development", Search = new FakeBookSearch(), RealAiAdmin = realAdmin };

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body = null, string? socketIp = null, params (string, string)[] headers)
    {
        var request = Wire.Get(path, socketIp, headers);
        request.Method = method;
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/ai" },
        { "PUT", "/api/admin/ai" },
        { "PUT", "/api/admin/ai/keys/gemini" },
        { "DELETE", "/api/admin/ai/keys/gemini" },
        { "POST", "/api/admin/ai/probe/gemini" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task OutsideDevelopment_EveryAdminRouteIs404(string method, string path)
    {
        await using var api = new ApiFactory { Search = new FakeBookSearch() };
        var response = await api.Client().SendAsync(Request(new HttpMethod(method), path, new { api_key = Key }), TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
        api.AiAdmin.LastKeySet.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task InDevelopment_ARemoteCallerGets404_EvenWithAMalformedBody(string method, string path)
    {
        await using var api = Dev();
        var request = Request(new HttpMethod(method), path, socketIp: "192.0.2.10");
        request.Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");

        await (await api.Client().SendAsync(request, TestContext.Current.CancellationToken)).ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
        api.Logs.Named("admin.refused").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task InDevelopment_AWrongMethod_RevealsNothingToARemoteCaller_ButIs405Locally()
    {
        await using var api = Dev();
        var client = api.Client();

        var remote = await client.SendAsync(Request(HttpMethod.Patch, "/api/admin/ai", socketIp: "192.0.2.10"), TestContext.Current.CancellationToken);
        await remote.ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
        remote.Content.Headers.Allow.ShouldBeEmpty();

        var local = await client.SendAsync(Request(HttpMethod.Patch, "/api/admin/ai"), TestContext.Current.CancellationToken);
        local.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        local.Content.Headers.Allow.ShouldBe(["GET", "PUT"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.5")]
    [InlineData("Forwarded", "for=203.0.113.5")]
    [InlineData("X-Real-IP", "203.0.113.5")]
    public async Task InDevelopment_ALoopbackPeerRelayingForSomeoneElse_Gets404(string header, string value)
    {
        await using var api = Dev();
        var response = await api.Client().SendAsync(Request(HttpMethod.Get, "/api/admin/ai", headers: (header, value)), TestContext.Current.CancellationToken);
        await response.ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("cross-site Origin", "Origin", "https://evil.example")]
    [InlineData("same host, other port", "Origin", "http://localhost:5173")]
    [InlineData("opaque Origin", "Origin", "null")]
    [InlineData("Sec-Fetch-Site cross-site", "Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site same-site", "Sec-Fetch-Site", "same-site")]
    public async Task InDevelopment_ABrowserRequestFromAnotherSite_Gets404_AndSpendsNothing(string caseId, string header, string value)
    {
        await using var api = Dev();
        var response = await api.Client().SendAsync(Request(HttpMethod.Post, "/api/admin/ai/probe/gemini", headers: (header, value)),
            TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
        api.Logs.Named("admin.refused").ShouldHaveSingleItem(caseId);
    }

    [Theory]
    [InlineData("no browser headers (curl, Scalar)", null, null)]
    [InlineData("same origin", "Origin", "http://localhost")]
    [InlineData("Sec-Fetch-Site same-origin", "Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site none (typed in the address bar)", "Sec-Fetch-Site", "none")]
    public async Task InDevelopment_SameOriginAndNonBrowserCalls_AreAllowed(string caseId, string? header, string? value)
    {
        await using var api = Dev();
        var headers = header is null ? Array.Empty<(string, string)>() : [(header, value!)];
        var response = await api.Client().SendAsync(Request(HttpMethod.Post, "/api/admin/ai/probe/gemini", headers: headers),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, caseId);
    }

    [Fact]
    public async Task InDevelopment_TheViteProxy_IsAllowed()
    {
        // changeOrigin: false keeps Host = localhost:5173, matching the page's Origin; the browser says same-origin.
        await using var api = Dev();
        var request = Request(HttpMethod.Post, "/api/admin/ai/probe/gemini",
            headers: [("Origin", "http://localhost:5173"), ("Sec-Fetch-Site", "same-origin")]);
        request.Headers.Host = "localhost:5173";
        (await api.Client().SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InDevelopment_FromLoopback_StatusIsMasked()
    {
        await using var api = Dev();
        var response = await api.Client().GetAsync("/api/admin/ai", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await response.Read<AdminAiStatus>();
        status.ActiveProvider.ShouldBe("gemini");
        status.Providers.Select(p => p.Name).ShouldBe(["gemini", "anthropic", "openai"], ignoreOrder: true);
    }

    [Fact]
    public async Task SetKey_IsAcceptedButNeverEchoedOrLogged()
    {
        await using var api = Dev();
        var client = api.Client();

        var set = await client.SendAsync(Request(HttpMethod.Put, "/api/admin/ai/keys/gemini", new { api_key = Key }), TestContext.Current.CancellationToken);
        var setBody = await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        set.StatusCode.ShouldBe(HttpStatusCode.OK, setBody);
        api.AiAdmin.LastKeySet.ShouldBe(Key);
        setBody.ShouldNotContain(Key);
        setBody.ShouldNotContain(KeyCore);
        var status = System.Text.Json.JsonSerializer.Deserialize<AdminAiStatus>(setBody, Wire.StrictContract)!;
        var gemini = status.Providers.Single(p => p.Name == "gemini");
        gemini.Configured.ShouldBeTrue();
        gemini.KeyFingerprint.ShouldNotBeNull().ShouldStartWith("sha256:");

        var get = await client.GetStringAsync("/api/admin/ai", TestContext.Current.CancellationToken);
        get.ShouldNotContain(Key);
        api.Logs.Entries.ShouldNotContain(e => e.AllText.Contains(KeyCore));
        api.Logs.Named("admin.ai.changed").ShouldHaveSingleItem().AllText.ShouldContain("gemini");

        var cleared = await client.SendAsync(Request(HttpMethod.Delete, "/api/admin/ai/keys/gemini"), TestContext.Current.CancellationToken);
        (await cleared.Read<AdminAiStatus>()).Providers.Single(p => p.Name == "gemini").Configured.ShouldBeFalse();
    }

    [Theory]
    [InlineData("missing key", "{}", "ApiKey")]
    [InlineData("too short", """{"api_key":"abc"}""", "ApiKey")]
    [InlineData("contains spaces", """{"api_key":"abc def ghi jkl"}""", "ApiKey")]
    public async Task SetKey_Rejects_BadKeys_WithoutEchoingThem(string caseId, string json, string field)
    {
        await using var api = Dev();
        var request = Request(HttpMethod.Put, "/api/admin/ai/keys/gemini");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await api.Client().SendAsync(request, TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors(field).ShouldNotBeEmpty(caseId);
        problem.ToJsonString().ShouldNotContain("ghi jkl");
        api.AiAdmin.LastKeySet.ShouldBeNull();
    }

    [Theory]
    [InlineData("PUT", "/api/admin/ai/keys/cohere")]
    [InlineData("DELETE", "/api/admin/ai/keys/GEMINI")]
    [InlineData("POST", "/api/admin/ai/probe/judge")]
    [InlineData("POST", "/api/admin/ai/probe/auto")]
    [InlineData("PUT", "/api/admin/ai/keys/auto")]
    public async Task UnknownProvider_Is400_NamingProvider(string method, string path)
    {
        await using var api = Dev();
        var response = await api.Client().SendAsync(Request(new HttpMethod(method), path, new { api_key = Key }), TestContext.Current.CancellationToken);
        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("Provider").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task UpdateSettings_SwitchesProviderModelAndJudge()
    {
        await using var api = Dev();
        var response = await api.Client().SendAsync(Request(HttpMethod.Put, "/api/admin/ai",
            new { provider = "anthropic", models = new Dictionary<string, string> { ["anthropic"] = "claude-sonnet-5-5" }, judge_enabled = true }),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await response.Read<AdminAiStatus>();
        status.ActiveProvider.ShouldBe("anthropic");
        status.Providers.Single(p => p.Name == "anthropic").Model.ShouldBe("claude-sonnet-5-5");
        status.Judge.Enabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("unknown provider", """{"provider":"cohere"}""", "Provider")]
    [InlineData("bad model id", """{"models":{"gemini":"gemini 3; drop"}}""", "Models.gemini")]
    [InlineData("model key is not a provider name", """{"models":{"Gemini!":"x"}}""", "Models")]
    public async Task UpdateSettings_RejectsBadInput(string caseId, string json, string field)
    {
        await using var api = Dev();
        var request = Request(HttpMethod.Put, "/api/admin/ai");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var problem = await (await api.Client().SendAsync(request, TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors(field).ShouldNotBeEmpty(caseId);
    }

    [Fact]
    public async Task Probe_RedactsAnythingKeyShaped_FromTheProvidersText()
    {
        await using var api = Dev();
        api.AiAdmin.ProbeDetail = "401: Incorrect API key provided: sk-proj-abcdefghijklmnopqrstuvwxyz012345";
        var response = await api.Client().SendAsync(Request(HttpMethod.Post, "/api/admin/ai/probe/openai"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var probe = await response.Read<AdminProbeResult>();
        probe.Detail.ShouldBe("401: Incorrect API key provided: [redacted]");
        probe.Provider.ShouldBe("openai");
    }

    [Fact]
    public async Task RealSettingsStore_KeepsKeysWriteOnly()
    {
        await using var api = Dev(realAdmin: true);
        _ = api.Server; // boot
        if (api.Services.GetService<IAiAdmin>() is null or FakeAiAdmin)
            Assert.Skip("No IAiAdmin is registered by the composition root.");
        var client = api.Client();

        var set = await client.SendAsync(Request(HttpMethod.Put, "/api/admin/ai/keys/anthropic", new { api_key = Key }), TestContext.Current.CancellationToken);
        var body = await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        set.StatusCode.ShouldBe(HttpStatusCode.OK, "PUT key failed");
        body.ShouldNotContain(Key);
        var anthropic = System.Text.Json.JsonSerializer.Deserialize<AdminAiStatus>(body, Wire.StrictContract)!.Providers.Single(p => p.Name == "anthropic");
        anthropic.KeySource.ShouldBe("runtime");
        anthropic.KeyFingerprint.ShouldNotBeNull();
        (await client.GetStringAsync("/api/admin/ai", TestContext.Current.CancellationToken)).ShouldNotContain(Key);
        api.Logs.Entries.ShouldNotContain(e => e.AllText.Contains(KeyCore));
    }
}
