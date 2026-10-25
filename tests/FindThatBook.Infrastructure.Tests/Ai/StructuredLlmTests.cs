using System.Net;
using Anthropic.Exceptions;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace FindThatBook.Infrastructure.Tests.Ai;

public sealed class StructuredLlmTests
{
    private const string Good = """{"title":"The Hobbit","tags":["dragon"]}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ok_parses_the_output_and_records_usage()
    {
        var client = FakeChatClient.Json(Good, input: 412, output: 37);
        await using var host = new AiTestHost(client);

        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        result.Call.Status.ShouldBe(LlmCallStatus.Ok);
        result.Value!.Title.ShouldBe("The Hobbit");
        result.Call.Provider.ShouldBe("gemini");
        result.Call.Model.ShouldBe("gemini-3.5-flash-lite");
        result.Call.PromptId.ShouldBe("test.v1");
        result.Call.CacheHit.ShouldBeFalse();
        result.Call.InputTokens.ShouldBe(412);
        result.Call.OutputTokens.ShouldBe(37);
        client.LastMessages!.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
        client.LastMessages![1].Text.ShouldBe("\"the hobbit\"");
    }

    [Fact]
    public async Task Identical_call_is_a_counted_cache_hit_with_no_tokens()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client);

        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        var second = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        client.Calls.ShouldBe(1);
        second.Call.Status.ShouldBe(LlmCallStatus.Ok);
        second.Call.CacheHit.ShouldBeTrue();
        second.Call.InputTokens.ShouldBe(0);
        second.Value!.Tags.ShouldBe(["dragon"]);
    }

    [Fact]
    public async Task Cache_bypass_setting_calls_the_provider_every_time()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new() { ["AI:Cache:Enabled"] = "false" });

        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        var second = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        client.Calls.ShouldBe(2);
        second.Call.CacheHit.ShouldBeFalse();
    }

    [Fact]
    public async Task Failure_is_never_cached()
    {
        var calls = 0;
        var client = new FakeChatClient((_, _, _) => Task.FromResult(FakeChatClient.Response(++calls == 1 ? "not json" : Good)));
        await using var host = new AiTestHost(client);

        var first = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        var second = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        first.Call.Status.ShouldBe(LlmCallStatus.MalformedOutput);
        first.Value.ShouldBeNull();
        second.Call.Status.ShouldBe(LlmCallStatus.Ok);
        second.Call.CacheHit.ShouldBeFalse();
        client.Calls.ShouldBe(2);
    }

    public static TheoryData<string, string, LlmCallStatus> BadAnswers => new()
    {
        { "malformed JSON", "{\"title\": \"The Hob", LlmCallStatus.MalformedOutput },
        { "prose instead of JSON", "Sure! The book is The Hobbit.", LlmCallStatus.MalformedOutput },
        { "empty text", "", LlmCallStatus.MalformedOutput },
    };

    [Theory]
    [MemberData(nameof(BadAnswers))]
    public async Task Unparseable_answer_is_malformed_output(string because, string text, LlmCallStatus expected)
    {
        await using var host = new AiTestHost(FakeChatClient.Json(text));
        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Status.ShouldBe(expected, because);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Finish_length_is_truncated_even_if_the_text_parses_AI01()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good, ChatFinishReason.Length));
        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Status.ShouldBe(LlmCallStatus.Truncated);
        result.Call.Detail.ShouldBe("finish=length");
        result.Call.InputTokens.ShouldBe(120); // tokens were spent, so they are reported
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Content_filter_and_refusal_content_are_refused_AI11()
    {
        await using (var host = new AiTestHost(FakeChatClient.Json("", ChatFinishReason.ContentFilter)))
            (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.Refused);

        var refusal = new ChatResponse(new ChatMessage(ChatRole.Assistant, [new ErrorContent("I can't help with that.") { ErrorCode = "Refusal" }]))
        {
            FinishReason = ChatFinishReason.Stop,
        };
        await using (var host = new AiTestHost(new FakeChatClient((_, _, _) => Task.FromResult(refusal))))
            (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.Refused);
    }

    [Fact]
    public async Task Timeout_comes_back_as_a_status_not_an_exception()
    {
        AiTestHost? hostRef = null;
        var client = new FakeChatClient(async (_, _, ct) =>
        {
            hostRef!.Time.Advance(TimeSpan.FromSeconds(10)); // fires the per-call deadline (TimeProvider timer)
            await Task.Delay(Timeout.Infinite, ct);
            return FakeChatClient.Response(Good);
        });
        await using var host = new AiTestHost(client);
        hostRef = host;

        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(timeout: TimeSpan.FromSeconds(4)), Ct);

        result.Call.Status.ShouldBe(LlmCallStatus.Timeout);
        result.Call.Detail.ShouldBe("timeout 4000ms");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var client = new FakeChatClient(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeChatClient.Response(Good);
        });
        await using var host = new AiTestHost(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() => host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), cts.Token));
    }

    public static TheoryData<string, Exception> RateLimits => new()
    {
        { "generic HTTP 429", new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests) },
        { "Gemini ApiException 429", new Google.GenAI.ClientError("Resource exhausted\nDetails: {\"retryDelay\": \"7s\"}", 429, "RESOURCE_EXHAUSTED") },
        { "Anthropic rate limit", new AnthropicRateLimitException { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" } },
    };

    [Theory]
    [MemberData(nameof(RateLimits))]
    public async Task Provider_429_starts_a_cooldown_that_skips_the_LLM_at_once_AI56(string because, Exception error)
    {
        var client = FakeChatClient.Throws(error);
        await using var host = new AiTestHost(client, new() { ["AI:Cache:Enabled"] = "false" });

        var first = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"a\""), Ct);
        var second = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"b\""), Ct);

        first.Call.Status.ShouldBe(LlmCallStatus.RateLimited, because); // the provider's own 429: a sent call
        second.Call.Status.ShouldBe(LlmCallStatus.BudgetExhausted, because); // skipped by our cooldown: nothing sent
        second.Call.Detail.ShouldBe("cooldown", because);
        client.Calls.ShouldBe(1, because);

        host.Time.Advance(TimeSpan.FromSeconds(31));
        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"c\""), Ct);
        client.Calls.ShouldBe(2, because);
    }

    [Fact]
    public async Task Gemini_retry_delay_sets_the_cooldown_length()
    {
        StructuredLlm.GeminiRetryDelay("Quota exceeded\nDetails: [{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"34s\"}]")
            .ShouldBe(TimeSpan.FromSeconds(34));
        StructuredLlm.GeminiRetryDelay("no details").ShouldBeNull();

        var client = FakeChatClient.Throws(new Google.GenAI.ClientError("x\nDetails: {\"retryDelay\":\"90s\"}", 429, "RESOURCE_EXHAUSTED"));
        await using var host = new AiTestHost(client, new() { ["AI:Cache:Enabled"] = "false" });
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Detail.ShouldBe("http 429, cooldown 90s");
        host.Time.Advance(TimeSpan.FromSeconds(89));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Detail.ShouldBe("cooldown");
        host.Time.Advance(TimeSpan.FromSeconds(1));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Detail.ShouldBe("http 429, cooldown 90s"); // sent again
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "http 401")]
    [InlineData(HttpStatusCode.Forbidden, "http 403")]
    [InlineData(HttpStatusCode.NotFound, "http 404")]
    public async Task Auth_or_unknown_model_switches_the_provider_off_AI55(HttpStatusCode status, string detail)
    {
        var client = FakeChatClient.Throws(new HttpRequestException("no", null, status));
        await using var host = new AiTestHost(client, new() { ["AI:Cache:Enabled"] = "false" });

        var first = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        var second = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        first.Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        first.Call.Detail.ShouldBe(detail);
        second.Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
        client.Calls.ShouldBe(1);
        host.Admin.GetStatus().AiConfigured.ShouldBeFalse();

        // A new key is a new target: the provider is tried again.
        host.Admin.SetKey("gemini", "another-key-9999");
        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        client.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Other_http_errors_are_provider_errors_and_do_not_disable(HttpStatusCode status)
    {
        var client = FakeChatClient.Throws(new HttpRequestException("no", null, status));
        await using var host = new AiTestHost(client, new() { ["AI:Cache:Enabled"] = "false" });
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        client.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_client_factory_is_a_provider_error_never_thrown()
    {
        var factory = new ThrowingFactory();
        await using var host = new AiTestHost(null!, factory: factory);
        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        result.Call.Detail.ShouldBe("InvalidOperationException");
    }

    private sealed class ThrowingFactory : IChatClientFactory
    {
        public IChatClient Get(ProviderTarget target) => throw new InvalidOperationException("SDK init failed");
    }

    [Fact]
    public async Task Unexpected_exception_is_a_provider_error_never_thrown()
    {
        await using var host = new AiTestHost(FakeChatClient.Throws(new IOException("socket")));
        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        result.Call.Detail.ShouldBe("IOException");
    }

    [Theory]
    [InlineData("none", "provider_none")]
    [InlineData("anthropic", "no_api_key")]
    public async Task Missing_provider_or_key_is_not_configured_without_a_call(string provider, string detail)
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new() { ["AI:Provider"] = provider, ["AI:Anthropic:ApiKey"] = "" });

        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);

        result.Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
        result.Call.Detail.ShouldBe(detail);
        client.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Requests_per_minute_budget_refuses_extra_calls_but_cache_hits_still_work()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new() { ["AI:Gemini:RequestsPerMinute"] = "2" });
        host.Settings.Options.Gemini.RequestsPerMinute.ShouldBe(2); // the key bound: the limit below is real, not vacuous

        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"a\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"b\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        var third = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"c\""), Ct);
        var hit = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"a\""), Ct);

        third.Call.Status.ShouldBe(LlmCallStatus.BudgetExhausted);
        third.Call.Detail.ShouldBe("rpm");
        hit.Call.CacheHit.ShouldBeTrue();
        client.Calls.ShouldBe(2);

        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"c\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
    }

    [Fact]
    public async Task Test_host_turns_every_provider_limit_off()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        var o = host.Settings.Options;
        (o.Gemini.RequestsPerMinute, o.Anthropic.RequestsPerMinute, o.OpenAI.RequestsPerMinute).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Explain_gives_way_to_interpret_when_few_permits_are_left()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new() { ["AI:Gemini:RequestsPerMinute"] = "5", ["AI:Budget:ExplainReserve"] = "3" });
        (host.Settings.Options.Gemini.RequestsPerMinute, host.Settings.Options.Budget.ExplainReserve).ShouldBe((5, 3));

        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"e1\"", operation: "explain"), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok); // 5 left
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"e2\"", operation: "explain"), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok); // 4 left
        var refused = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"e3\"", operation: "explain"), Ct);    // 3 left: kept for interpret
        refused.Call.Status.ShouldBe(LlmCallStatus.BudgetExhausted);
        refused.Call.Detail.ShouldBe("explain_reserve");
        for (var i = 1; i <= 3; i++)
            (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call($"\"i{i}\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"i4\""), Ct)).Call.Detail.ShouldBe("rpm");
        client.Calls.ShouldBe(5);
    }

    [Fact]
    public async Task Requests_per_minute_are_counted_per_provider()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new() { ["AI:Gemini:RequestsPerMinute"] = "1", ["AI:Anthropic:RequestsPerMinute"] = "1" });
        (host.Settings.Options.Gemini.RequestsPerMinute, host.Settings.Options.Anthropic.RequestsPerMinute).ShouldBe((1, 1));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"a\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"b\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.BudgetExhausted);
        host.Admin.Update(new AiSettingsUpdate(Provider: "anthropic"));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call("\"b\""), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok); // its own window
    }

    [Fact]
    public void Quota_id_and_value_are_read_from_the_gemini_429()
    {
        var message = "got status: RESOURCE_EXHAUSTED. You exceeded your current quota.\nDetails: "
            + "{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaMetric\":\"generativelanguage.googleapis.com/generate_content_free_tier_requests\","
            + "\"quotaId\":\"GenerateRequestsPerMinutePerProjectPerModel-FreeTier\",\"quotaDimensions\":{\"model\":\"gemini-3.5-flash-lite\"},\"quotaValue\":\"15\"}]}\n"
            + "{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"36s\"}";
        var error = new Google.GenAI.ClientError(message, 429, "RESOURCE_EXHAUSTED");
        StructuredLlm.QuotaOf(error).ShouldBe(("GenerateRequestsPerMinutePerProjectPerModel-FreeTier", "15"));
        StructuredLlm.HttpStatusOf(error).ShouldBe((429, TimeSpan.FromSeconds(36)));
        StructuredLlm.QuotaOf(new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests)).ShouldBe((null, null));
    }

    [Fact]
    public async Task An_unpriced_model_still_counts_against_the_daily_cap_at_the_dearest_price()
    {
        // 100k output tokens at the dearest output price in the table ($10/MTok) = $1 > the $0.50 cap.
        var client = FakeChatClient.Json(Good, input: 0, output: 100_000);
        await using var host = new AiTestHost(client, new() { ["AI:Gemini:Model"] = "gemini-9-unpriced", ["AI:Cache:Enabled"] = "false" });
        host.Settings.Options.Budget.DailyCostCapUsd.ShouldBe(0.50m);

        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        host.Budget.SpentToday.ShouldBe(1.00m);
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Detail.ShouldBe("daily_cap");
        host.Get<IAiPricing>().Estimate("gemini-9-unpriced", 0, 100_000).ShouldBeNull(); // responses still say "unknown"
    }

    [Fact]
    public async Task Daily_cost_cap_refuses_calls_until_the_next_utc_day_SY08()
    {
        // 1M input tokens on gemini-3.5-flash-lite = $0.30 > the $0.10 cap.
        var client = FakeChatClient.Json(Good, input: 1_000_000, output: 0);
        await using var host = new AiTestHost(client, new() { ["AI:Budget:DailyCostCapUsd"] = "0.10", ["AI:Cache:Enabled"] = "false" });

        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
        var refused = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        refused.Call.Status.ShouldBe(LlmCallStatus.BudgetExhausted);
        refused.Call.Detail.ShouldBe("daily_cap");
        host.Budget.SpentToday.ShouldBe(0.30m);

        host.Time.Advance(TimeSpan.FromHours(12)); // 2026-10-25 00:00 UTC
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.Ok);
    }

    public static TheoryData<string, StructuredCall, string, string> KeyChanges => new()
    {
        { "user text", AiTestHost.Call(user: "\"the hobbit!\""), "gemini", "gemini-3.5-flash-lite" },
        { "system prompt", AiTestHost.Call(system: "system v2"), "gemini", "gemini-3.5-flash-lite" },
        { "prompt id", AiTestHost.Call(promptId: "test.v2"), "gemini", "gemini-3.5-flash-lite" },
        { "operation", AiTestHost.Call(operation: "explain"), "gemini", "gemini-3.5-flash-lite" },
        { "provider", AiTestHost.Call(), "anthropic", "gemini-3.5-flash-lite" },
        { "model", AiTestHost.Call(), "gemini", "gemini-3.8-flash" },
    };

    [Theory]
    [MemberData(nameof(KeyChanges))]
    public void Cache_key_changes_with_everything_that_is_sent_AI30(string because, StructuredCall call, string provider, string model)
    {
        var baseline = StructuredLlm.CacheKey(AiTestHost.Call(), Target("gemini", "gemini-3.5-flash-lite"), "schema1");
        var changed = StructuredLlm.CacheKey(call, Target(provider, model), "schema1");
        changed.ShouldNotBe(baseline, because);
        StructuredLlm.CacheKey(AiTestHost.Call(), Target("gemini", "gemini-3.5-flash-lite"), "schema2").ShouldNotBe(baseline, "schema version");
    }

    [Fact]
    public void Cache_key_is_bounded_and_does_not_contain_the_text_SY23()
    {
        var longCjk = "\"" + string.Concat(Enumerable.Repeat("三体问题", 75)) + "\"";
        var key = StructuredLlm.CacheKey(AiTestHost.Call(user: longCjk), Target("gemini", "m"), "s");
        key.Length.ShouldBeLessThan(100);
        key.ShouldStartWith("ai:interpret:");
        key.ShouldNotContain("三体");
        StructuredLlm.CacheKey(AiTestHost.Call(user: longCjk), Target("gemini", "m"), "s").ShouldBe(key);
    }

    [Fact]
    public async Task Same_user_text_with_another_model_is_a_separate_cache_entry()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client);
        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        host.Admin.Update(new AiSettingsUpdate(Models: new Dictionary<string, string> { ["gemini"] = "gemini-3.8-flash" }));
        var other = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        other.Call.CacheHit.ShouldBeFalse();
        other.Call.Model.ShouldBe("gemini-3.8-flash");
        client.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Token_cap_is_the_larger_of_the_call_and_the_provider_floor()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client);
        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(maxTokens: 256), Ct);
        client.LastOptions!.MaxOutputTokens.ShouldBe(512); // Gemini interpret floor (thinking shares the cap, AI-05)
        await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(user: "\"x\"", maxTokens: 2000), Ct);
        client.LastOptions!.MaxOutputTokens.ShouldBe(2000);
    }

    [Fact]
    public async Task Oversized_prompt_is_refused_before_any_call()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client);
        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(user: new string('x', StructuredLlm.MaxPromptChars)), Ct);
        result.Call.Status.ShouldBe(LlmCallStatus.ProviderError);
        result.Call.Detail.ShouldBe("prompt_too_large");
        client.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_identical_calls_share_one_provider_call_SY20()
    {
        var gate = new TaskCompletionSource();
        var client = new FakeChatClient(async (_, _, _) =>
        {
            await gate.Task;
            return FakeChatClient.Response(Good);
        });
        await using var host = new AiTestHost(client);
        var calls = Enumerable.Range(0, 10).Select(_ => host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        client.Calls.ShouldBe(1);
        results.ShouldAllBe(r => r.Call.Status == LlmCallStatus.Ok);
        results.Count(r => !r.Call.CacheHit).ShouldBe(1);
    }

    [Theory]
    [InlineData("Thinking level MINIMAL is not supported for this model.\nDetails: x", "Thinking level MINIMAL is not supported for this model.")]
    [InlineData("Incorrect API key provided: sk-proj-******d000.", "Incorrect API key provided: [redacted].")]
    [InlineData("API key not valid: AIzaSyA1b2C3d4E5f6G7h8", "API key not valid: [redacted]")]
    [InlineData("token abcdefghijklmnopqrstuvwxyz0123456789ABCD rejected", "token [redacted] rejected")]
    public void Logged_400_excerpt_is_one_line_and_masks_key_shapes_AI09_AI49(string message, string expected) =>
        SecretRedaction.Excerpt(message).ShouldBe(expected);

    private static ProviderTarget Target(string provider, string model) =>
        new(provider, model, new ResolvedKey("k", KeySources.AppSettings), new GeminiOptions());
}
