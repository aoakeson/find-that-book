using System.Net;
using System.Text.Json;
using Anthropic.Models.Messages;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace FindThatBook.Infrastructure.Tests.Ai;

/// <summary>The per-provider guards (AI-01, AI-02, AI-05, AI-06, AI-10), checked on the options and on the real SDKs' request bodies.</summary>
public sealed class ProviderOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Gemini_minimal_thinking_no_temperature_no_hidden_continuation_AI05()
    {
        var options = ProviderChatOptions.Build(Target(AiProviders.Gemini, new GeminiOptions(), "gemini-3.5-flash-lite"), 512);
        options.Temperature.ShouldBeNull();
        options.MaxOutputTokens.ShouldBe(512);
        var config = options.RawRepresentationFactory!(null!).ShouldBeOfType<GenerateContentConfig>();
        config.AutomaticContinuation.ShouldBe(false);
        config.ThinkingConfig!.ThinkingLevel.ShouldBe(ThinkingLevel.Minimal);
        config.ThinkingConfig.ThinkingBudget.ShouldBeNull();
    }

    [Fact]
    public void Gemini_2_5_budget_knob_is_sent_only_when_configured()
    {
        var o = new GeminiOptions { ThinkingLevel = null, ThinkingBudget = 0 };
        var config = (GenerateContentConfig)ProviderChatOptions.Build(Target(AiProviders.Gemini, o, "gemini-2.5-flash-lite"), 512).RawRepresentationFactory!(null!)!;
        config.ThinkingConfig!.ThinkingBudget.ShouldBe(0);
        config.ThinkingConfig.ThinkingLevel.ShouldBeNull();

        var none = (GenerateContentConfig)ProviderChatOptions.Build(Target(AiProviders.Gemini, new GeminiOptions { ThinkingLevel = null }, "m"), 512).RawRepresentationFactory!(null!)!;
        none.ThinkingConfig.ShouldBeNull();
    }

    [Fact]
    public void Anthropic_never_sends_temperature_and_disables_thinking_at_low_effort_AI02_AI06()
    {
        var o = new AnthropicOptions { Temperature = 0 }; // even if someone configures it
        var options = ProviderChatOptions.Build(Target(AiProviders.Anthropic, o, "claude-haiku-5-5"), 1024);
        options.Temperature.ShouldBeNull();
        var raw = options.RawRepresentationFactory!(null!).ShouldBeOfType<MessageCreateParams>();
        raw.Model.Raw().ShouldBe("claude-haiku-5-5");
        raw.MaxTokens.ShouldBe(1024);
        raw.Thinking!.Value.ShouldBeOfType<ThinkingConfigDisabled>();
        raw.OutputConfig!.Effort!.Value().ShouldBe(Effort.Low);
    }

    [Fact]
    public void Anthropic_adaptive_thinking_is_a_config_switch()
    {
        var raw = (MessageCreateParams)ProviderChatOptions.Build(Target(AiProviders.Anthropic, new AnthropicOptions { Thinking = "adaptive", Effort = null }, "claude-haiku-5-5"), 1024)
            .RawRepresentationFactory!(null!)!;
        raw.Thinking!.Value.ShouldBeOfType<ThinkingConfigAdaptive>();
        raw.OutputConfig.ShouldBeNull();
    }

    [Theory]
    [InlineData("gpt-4.1-mini", false)]
    [InlineData("gpt-4.1-nano", false)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("gpt-5-nano", true)]
    [InlineData("gpt-5.1-mini", true)]
    [InlineData("o4-mini", true)]
    public void OpenAI_reasoning_models_get_no_temperature_and_minimal_effort_AI01_AI02(string model, bool reasoning)
    {
        OpenAiModels.IsReasoning(model).ShouldBe(reasoning);
        var options = ProviderChatOptions.Build(Target(AiProviders.OpenAi, new OpenAiOptions(), model), 512);
        options.AdditionalProperties!["strict"].ShouldBe(true);
        if (reasoning)
        {
            options.Temperature.ShouldBeNull();
            var raw = options.RawRepresentationFactory!(null!).ShouldBeOfType<ChatCompletionOptions>();
#pragma warning disable OPENAI001
            raw.ReasoningEffortLevel!.Value.ToString().ShouldBe("minimal");
#pragma warning restore OPENAI001
        }
        else
        {
            options.Temperature.ShouldBe(0f);
            options.RawRepresentationFactory.ShouldBeNull();
        }
    }

    public static TheoryData<string, string> Providers => new()
    {
        { AiProviders.Gemini, "gemini-3.5-flash-lite" },
        { AiProviders.Anthropic, "claude-haiku-5-5" },
        { AiProviders.OpenAi, "gpt-4.1-mini" },
        { AiProviders.OpenAi, "gpt-5-nano" },
    };

    /// <summary>AI-10: one 429 must be one HTTP request (the OpenAI SDK's default was 4), through the real SDK clients.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Real_sdk_sends_exactly_one_request_on_429_AI10(string provider, string model)
    {
        var handler = new CountingHandler(HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"message":"Resource exhausted","status":"RESOURCE_EXHAUSTED","type":"rate_limit_error"}}""",
            r => r.Headers.TryAddWithoutValidation("Retry-After", "0"));
        var section = provider switch { AiProviders.Gemini => "Gemini", AiProviders.Anthropic => "Anthropic", _ => "OpenAI" };
        await using var host = new AiTestHost(null!, new() { ["AI:Provider"] = provider, [$"AI:{section}:Model"] = model, ["AI:Cache:Enabled"] = "false" },
            new ChatClientFactory(handler));

        var result = await host.Llm.CallAsync<InterpretOutput>(LlmQueryInterpreter.BuildCall("tolkien hobbit", host.Settings.Options), Ct);

        result.Call.Status.ShouldBe(LlmCallStatus.RateLimited);
        handler.Requests.ShouldBe(1);
    }

    /// <summary>The bodies the SDKs actually send: no temperature where it 400s, thinking minimized, schema attached.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Real_sdk_request_body_carries_the_guards(string provider, string model)
    {
        var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"down"}}""");
        var section = provider switch { AiProviders.Gemini => "Gemini", AiProviders.Anthropic => "Anthropic", _ => "OpenAI" };
        await using var host = new AiTestHost(null!, new() { ["AI:Provider"] = provider, [$"AI:{section}:Model"] = model, ["AI:Cache:Enabled"] = "false" },
            new ChatClientFactory(handler));

        (await host.Llm.CallAsync<InterpretOutput>(LlmQueryInterpreter.BuildCall("tolkien hobbit", host.Settings.Options), Ct))
            .Call.Status.ShouldBe(LlmCallStatus.ProviderError);

        handler.Requests.ShouldBe(1);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        var text = root.GetRawText();
        text.ShouldContain("english_title"); // the schema went out
        text.ShouldContain("tolkien hobbit"); // the query went out as data
        switch (provider)
        {
            case AiProviders.Gemini:
                var config = root.GetProperty("generationConfig");
                config.TryGetProperty("temperature", out _).ShouldBeFalse();
                config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString().ShouldBe("MINIMAL");
                config.GetProperty("responseMimeType").GetString().ShouldBe("application/json");
                config.GetProperty("maxOutputTokens").GetInt32().ShouldBe(512);
                break;
            case AiProviders.Anthropic:
                root.TryGetProperty("temperature", out _).ShouldBeFalse();
                root.GetProperty("thinking").GetProperty("type").GetString().ShouldBe("disabled");
                root.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("low");
                root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString().ShouldBe("json_schema");
                root.GetProperty("max_tokens").GetInt32().ShouldBe(1024);
                break;
            default:
                root.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean().ShouldBeTrue();
                if (OpenAiModels.IsReasoning(model))
                {
                    root.TryGetProperty("temperature", out _).ShouldBeFalse();
                    root.GetProperty("reasoning_effort").GetString().ShouldBe("minimal");
                }
                else
                {
                    root.GetProperty("temperature").GetDouble().ShouldBe(0);
                    root.TryGetProperty("reasoning_effort", out _).ShouldBeFalse();
                }
                break;
        }
    }

    private static ProviderTarget Target(string provider, ProviderOptions options, string model) =>
        new(provider, model, new ResolvedKey("k", KeySources.AppSettings), options);
}
