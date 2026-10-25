using System.Diagnostics;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Judge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FindThatBook.Infrastructure.Tests.Judge;

public sealed class OpenAiDecisionsJudgeTests : IDisposable
{
    private const string Key = "sk-test-judge-0000";
    private readonly WireMockServer _server = WireMockServer.Start();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JudgeRequest Request = new("tolkien hobbit illustrated deluxe 1937",
    [
        new("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], [], 1937, 482),
        new("OL219602W", "The Hobbit", ["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"], ["Charles Dixon (Adapter)"], 1990, 10),
        new("OL14926019W", "The Hobbit & The Lord of the Rings [collection/set]", ["J.R.R. Tolkien"], [], 1979, 27),
        new("OL14933471W", "Poems from The hobbit", ["J.R.R. Tolkien"], [], 1999, 3),
    ]);

    public void Dispose() => _server.Stop();

    private ServiceProvider Services(bool enabled = true, string? key = Key, int cacheMinutes = 0, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Judge:Enabled"] = enabled ? "true" : "false",
            ["Judge:BaseUrl"] = _server.Url,
            ["Judge:CacheMinutes"] = cacheMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["AI:OpenAI:ApiKey"] = key,
        };
        foreach (var (k, v) in extra ?? []) values[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton<IConfiguration>(config);
        services.AddJudge(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private void Reply(int status, string body, TimeSpan? delay = null)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        if (delay is { } d) response = response.WithDelay(d);
        _server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/v1/decisions").UsingPost()).RespondWith(response);
    }

    private int Hits => _server.LogEntries.Count();

    [Fact]
    public async Task Answer_is_parsed_and_the_key_sent_as_bearer()
    {
        Reply(200, DecisionsFixtures.HobbitChoice);
        await using var sp = Services();
        var judge = sp.GetRequiredService<IMatchJudge>();

        judge.ShouldBeOfType<OpenAiDecisionsJudge>();
        judge.IsEnabled.ShouldBeTrue();
        var v = await judge.JudgeAsync(Request, Ct);

        v.Status.ShouldBe(JudgeStatus.Agreed);
        v.Probability.ShouldBe(1.0);
        v.InputTokens.ShouldBe(833);
        var sent = _server.LogEntries.Single().RequestMessage!;
        sent.Headers!["Authorization"]!.Single().ShouldBe($"Bearer {Key}");
        sent.Body!.ShouldContain("\"best_match\"");
        sent.Body!.ShouldContain("\"NONE\"");
    }

    [Fact]
    public async Task Off_by_default_resolves_the_null_judge()
    {
        await using var sp = Services(enabled: false);
        var judge = sp.GetRequiredService<IMatchJudge>();
        judge.ShouldBeSameAs(NullMatchJudge.Instance);
        judge.IsEnabled.ShouldBeFalse();
        (await judge.JudgeAsync(Request, Ct)).Status.ShouldBe(JudgeStatus.Off);
    }

    [Fact]
    public async Task Admin_toggle_applies_on_the_next_resolution()
    {
        Reply(200, DecisionsFixtures.HobbitChoice);
        await using var sp = Services(enabled: false);
        sp.GetRequiredService<IMatchJudge>().IsEnabled.ShouldBeFalse();
        sp.GetRequiredService<AiSettingsStore>().SetJudgeEnabled(true);
        sp.GetRequiredService<IMatchJudge>().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task No_key_means_not_enabled()
    {
        await using var sp = Services(key: null);
        var judge = sp.GetRequiredService<IMatchJudge>();
        judge.IsEnabled.ShouldBeFalse();
        (await judge.JudgeAsync(Request, Ct)).Status.ShouldBe(JudgeStatus.Off);
        Hits.ShouldBe(0);
    }

    [Fact]
    public async Task Judge_key_falls_back_to_the_openai_key_but_its_own_wins()
    {
        Reply(200, DecisionsFixtures.HobbitChoice);
        await using var sp = Services(extra: new() { ["Judge:ApiKey"] = "sk-judge-own-1" });
        await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        _server.LogEntries.Single().RequestMessage!.Headers!["Authorization"]!.Single().ShouldBe("Bearer sk-judge-own-1");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Auth_or_model_error_disables_the_judge_for_the_process_AI49(int status)
    {
        Reply(status, DecisionsFixtures.InvalidKey);
        await using var sp = Services();

        var first = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        var judge = sp.GetRequiredService<IMatchJudge>();
        var second = await judge.JudgeAsync(Request, Ct);

        first.Status.ShouldBe(JudgeStatus.Unavailable);
        first.Detail.ShouldBe($"http {status}");
        judge.IsEnabled.ShouldBeFalse();
        second.Status.ShouldBe(JudgeStatus.Unavailable);
        Hits.ShouldBe(1); // not retried, and not called again
    }

    [Fact]
    public async Task Bad_request_is_unavailable_and_not_retried()
    {
        Reply(400, DecisionsFixtures.DuplicateChoices);
        await using var sp = Services();
        var v = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        v.Status.ShouldBe(JudgeStatus.Unavailable);
        v.Detail.ShouldBe("http 400");
        Hits.ShouldBe(1);
        sp.GetRequiredService<IMatchJudge>().IsEnabled.ShouldBeTrue(); // a builder bug, not a dead key
    }

    [Fact]
    public async Task Server_error_is_not_retried()
    {
        Reply(503, """{"error":{"message":"overloaded"}}""");
        await using var sp = Services();
        var v = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        v.Status.ShouldBe(JudgeStatus.Unavailable);
        v.Detail.ShouldBe("http 503");
        Hits.ShouldBe(1); // one billed request, one recorded
    }

    [Fact]
    public async Task Rate_limit_is_one_request_then_a_skipped_cooldown()
    {
        _server.Given(WireMock.RequestBuilders.Request.Create().WithPath("/v1/decisions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "20").WithBody("""{"error":{"code":"rate_limit_exceeded"}}"""));
        await using var sp = Services();

        var first = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        var second = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);

        first.Status.ShouldBe(JudgeStatus.Unavailable);
        first.Detail.ShouldBe("http 429");
        second.Status.ShouldBe(JudgeStatus.Skipped);
        second.Detail.ShouldBe("cooldown");
        Hits.ShouldBe(1);
        sp.GetRequiredService<JudgeHealth>().InCooldown.ShouldBeTrue();
    }

    [Fact]
    public async Task Slow_upstream_is_cut_at_the_total_timeout()
    {
        Reply(200, DecisionsFixtures.HobbitChoice, TimeSpan.FromSeconds(5));
        await using var sp = Services();
        var sw = Stopwatch.StartNew();
        var v = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        sw.Stop();
        v.Status.ShouldBe(JudgeStatus.Unavailable);
        v.Detail.ShouldBe("timeout");
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Refusal_with_200_is_refused()
    {
        Reply(200, DecisionsFixtures.Refusal);
        await using var sp = Services();
        (await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct)).Status.ShouldBe(JudgeStatus.Refused);
    }

    [Fact]
    public async Task Fewer_than_two_candidates_is_skipped_without_a_call()
    {
        await using var sp = Services();
        var v = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request with { Candidates = [Request.Candidates[0], Request.Candidates[0]] }, Ct);
        v.Status.ShouldBe(JudgeStatus.Skipped);
        Hits.ShouldBe(0);
    }

    [Fact]
    public async Task Verdicts_are_cached_but_failures_are_not_AI54()
    {
        Reply(200, DecisionsFixtures.HobbitChoice);
        await using var sp = Services(cacheMinutes: 60);
        var first = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        var second = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        Hits.ShouldBe(1);
        first.Detail.ShouldBeNull();
        second.Detail.ShouldBe("cache_hit");
        second.InputTokens.ShouldBe(0);
        second.Pick.ShouldBe(first.Pick);

        _server.Reset();
        Reply(503, "{}");
        var other = Request with { Query = "the hobbit" };
        (await sp.GetRequiredService<IMatchJudge>().JudgeAsync(other, Ct)).Status.ShouldBe(JudgeStatus.Unavailable);
        (await sp.GetRequiredService<IMatchJudge>().JudgeAsync(other, Ct)).Status.ShouldBe(JudgeStatus.Unavailable);
        Hits.ShouldBe(2); // one attempt each time: nothing was cached
    }

    [Fact]
    public async Task Daily_cost_cap_skips_the_judge_SY08()
    {
        Reply(200, DecisionsFixtures.HobbitChoice);
        await using var sp = Services(extra: new() { ["Judge:DailyCostCapUsd"] = "0.00005" }); // one call = 833 tokens ≈ $0.0000833
        (await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct)).Status.ShouldBe(JudgeStatus.Agreed);
        var v = await sp.GetRequiredService<IMatchJudge>().JudgeAsync(Request, Ct);
        v.Status.ShouldBe(JudgeStatus.Skipped);
        v.Detail.ShouldBe("daily_cap");
        Hits.ShouldBe(1);
    }
}
