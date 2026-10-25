using Anthropic.Models.Messages;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Per-provider request options, in one place so each provider quirk is a unit-tested line:
/// no temperature where the API rejects it (AI-02), thinking/reasoning kept minimal so it cannot eat the token cap
/// or the 4 s budget (AI-01, AI-03, AI-05, AI-06), OpenAI strict schema, and Gemini's hidden re-requests off.
/// </summary>
public static class ProviderChatOptions
{
    public static ChatOptions Build(ProviderTarget target, int maxOutputTokens) => target.Provider switch
    {
        AiProviders.Gemini => Gemini((GeminiOptions)target.Options, target.Model, maxOutputTokens),
        AiProviders.Anthropic => Anthropic((AnthropicOptions)target.Options, target.Model, maxOutputTokens),
        AiProviders.OpenAi => OpenAi((OpenAiOptions)target.Options, target.Model, maxOutputTokens),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.Provider, "Unknown provider."),
    };

    private static ChatOptions Gemini(GeminiOptions o, string model, int maxOutputTokens) => new()
    {
        ModelId = model,
        MaxOutputTokens = maxOutputTokens,
        Temperature = (float?)o.Temperature,
        RawRepresentationFactory = _ =>
        {
            var config = new GenerateContentConfig
            {
                // A response cut at the token cap is re-requested by default (each billed): a hidden retry. Off, so a
                // truncated answer surfaces as finish=length (AI-01/AI-10).
                AutomaticContinuation = false,
            };
            var thinkingLevel = o.ThinkingLevelFor(model);
            if (thinkingLevel is not null || o.ThinkingBudget is not null)
            {
                config.ThinkingConfig = new ThinkingConfig
                {
                    // The struct converts implicitly from string, so a bare null would be read as a null string.
                    ThinkingLevel = thinkingLevel is { } level ? ThinkingLevel.FromString(level.ToUpperInvariant()) : (ThinkingLevel?)null,
                    ThinkingBudget = o.ThinkingBudget,
                };
            }
            return config;
        },
    };

    /// <remarks>
    /// Temperature is never sent: API version 2026-10-21 (SDK 13.x) rejects any sampling parameter on every model. The
    /// raw params carry model and max_tokens because the adapter takes them from here when a factory is supplied.
    /// </remarks>
    private static ChatOptions Anthropic(AnthropicOptions o, string model, int maxOutputTokens) => new()
    {
        ModelId = model,
        MaxOutputTokens = maxOutputTokens,
        RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxOutputTokens,
            Messages = [],
            Thinking = string.Equals(o.Thinking, "adaptive", StringComparison.OrdinalIgnoreCase)
                ? new ThinkingConfigAdaptive()
                : new ThinkingConfigDisabled(),
            OutputConfig = o.Effort is { } effort ? new OutputConfig { Effort = ParseEffort(effort) } : null,
        },
    };

    private static ChatOptions OpenAi(OpenAiOptions o, string model, int maxOutputTokens)
    {
        var reasoning = OpenAiModels.IsReasoning(model);
        var options = new ChatOptions
        {
            ModelId = model,
            MaxOutputTokens = maxOutputTokens,
            // Reasoning models 400 on temperature 0 (AI-02).
            Temperature = reasoning ? null : (float?)o.Temperature,
            AdditionalProperties = new() { ["strict"] = true },
        };
        if (reasoning && o.ReasoningEffort is { } effort)
        {
#pragma warning disable OPENAI001 // ReasoningEffortLevel is marked experimental in OpenAI 2.14
            options.RawRepresentationFactory = _ => new ChatCompletionOptions
            {
                ReasoningEffortLevel = new ChatReasoningEffortLevel(effort.ToLowerInvariant()),
            };
#pragma warning restore OPENAI001
        }
        return options;
    }

    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        _ => Effort.High,
    };
}

public static class OpenAiModels
{
    /// <summary>Reasoning families: no temperature, and reasoning tokens share the output cap (AI-01..AI-03).</summary>
    public static bool IsReasoning(string model)
    {
        var m = model.Trim().ToLowerInvariant();
        return m.StartsWith("gpt-5", StringComparison.Ordinal) || m.StartsWith("gpt-6", StringComparison.Ordinal)
            || (m.Length > 1 && m[0] == 'o' && char.IsAsciiDigit(m[1]));
    }
}
