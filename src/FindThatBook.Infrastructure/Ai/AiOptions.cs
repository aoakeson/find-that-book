namespace FindThatBook.Infrastructure.Ai;

/// <summary>Configuration section "AI". Keys arrive as AI:{Provider}:ApiKey (the API maps GEMINI_API_KEY etc.).</summary>
public sealed class AiOptions
{
    public const string Section = "AI";

    /// <summary>
    /// "auto" (default: the first of gemini → anthropic → openai that has a key, else none) | "gemini" | "anthropic" |
    /// "openai" | "none". An explicit name is used even without a key (then ai_not_configured).
    /// </summary>
    public string Provider { get; set; } = AiProviders.Auto;

    public GeminiOptions Gemini { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public OpenAiOptions OpenAI { get; set; } = new();

    /// <summary>Per-operation defaults: a call that passes no timeout / token cap gets these.</summary>
    public OperationOptions Interpret { get; set; } = new() { TimeoutMs = 4000, MaxOutputTokens = 256 };
    public OperationOptions Explain { get; set; } = new() { TimeoutMs = 5000, MaxOutputTokens = 600 };

    public AiCacheOptions Cache { get; set; } = new();
    public AiBudgetOptions Budget { get; set; } = new();

    /// <summary>
    /// Paid-tier USD per million tokens (list prices of 2026-10-24). The only price table: configuration may add or
    /// override models. Missing model → no estimate in responses.
    /// </summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-3.5-flash-lite"] = new() { InputPerMTok = 0.30m, OutputPerMTok = 2.50m },
        ["gemini-3.8-flash"] = new() { InputPerMTok = 0.75m, OutputPerMTok = 3.75m },
        ["gemini-2.5-flash-lite"] = new() { InputPerMTok = 0.10m, OutputPerMTok = 0.40m },
        ["claude-haiku-5-5"] = new() { InputPerMTok = 0.10m, OutputPerMTok = 0.50m },
        ["claude-sonnet-5-5"] = new() { InputPerMTok = 2.00m, OutputPerMTok = 10.00m },
        ["gpt-4.1-mini"] = new() { InputPerMTok = 0.40m, OutputPerMTok = 1.60m },
        ["gpt-4.1-nano"] = new() { InputPerMTok = 0.10m, OutputPerMTok = 0.40m },
        ["gpt-5-nano"] = new() { InputPerMTok = 0.05m, OutputPerMTok = 0.40m },
        ["gpt-6-luna"] = new() { InputPerMTok = 0.10m, OutputPerMTok = 0.50m },
    };

    public ProviderOptions? For(string provider) => provider switch
    {
        AiProviders.Gemini => Gemini,
        AiProviders.Anthropic => Anthropic,
        AiProviders.OpenAi => OpenAI,
        _ => null,
    };

    public OperationOptions ForOperation(string operation) =>
        string.Equals(operation, "explain", StringComparison.OrdinalIgnoreCase) ? Explain : Interpret;
}

public static class AiProviders
{
    public const string Gemini = "gemini";
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string None = "none";
    public const string Auto = "auto";

    /// <summary>Chat providers in auto-selection order (Gemini first: the brief's default).</summary>
    public static IReadOnlyList<string> All { get; } = [Gemini, Anthropic, OpenAi];

    /// <summary>Lower-cased provider setting ("auto", "none" or a provider name), or null when unknown.</summary>
    public static string? Normalize(string? name)
    {
        var n = name?.Trim().ToLowerInvariant();
        return n is Gemini or Anthropic or OpenAi or None or Auto ? n : null;
    }
}

public abstract class ProviderOptions
{
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }

    /// <summary>
    /// Per-operation token floor ("Interpret", "Explain"). The cap sent is max(call's cap, this floor), because
    /// thinking/reasoning tokens count against it on some providers (AI-01, AI-05, AI-06).
    /// </summary>
    public Dictionary<string, int> MaxOutputTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null = never sent. Ignored where the API rejects it (AI-02).</summary>
    public double? Temperature { get; set; }

    /// <summary>Provider calls per minute for this provider (cache hits are free). 0 = unlimited.</summary>
    public int RequestsPerMinute { get; set; }

    public int OutputFloor(string operation) => MaxOutputTokens.TryGetValue(operation, out var n) ? n : 0;
}

public sealed class GeminiOptions : ProviderOptions
{
    public GeminiOptions()
    {
        Model = "gemini-3.5-flash-lite";
        RequestsPerMinute = 15; // measured 2026-10-25: free tier GenerateRequestsPerMinutePerProjectPerModel-FreeTier = 15
        MaxOutputTokens["Interpret"] = 512;
        MaxOutputTokens["Explain"] = 1024;
    }

    /// <summary>Gemini 3.x knob (AI-05): "minimal" | "low" | "medium" | "high"; null = model default.</summary>
    public string? ThinkingLevel { get; set; } = "minimal";

    /// <summary>
    /// Per-model override of <see cref="ThinkingLevel"/>. gemini-3.8-flash answers HTTP 400 "Thinking level MINIMAL is
    /// not supported for this model" (measured 2026-10-24), so it gets its lowest accepted level.
    /// </summary>
    public Dictionary<string, string> ThinkingLevelByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-3.8-flash"] = "low",
    };

    public string? ThinkingLevelFor(string model) =>
        ThinkingLevelByModel.FirstOrDefault(kv => string.Equals(kv.Key, model, StringComparison.OrdinalIgnoreCase)).Value ?? ThinkingLevel;

    /// <summary>Gemini 2.5 knob: 0 = off. Sent only when set.</summary>
    public int? ThinkingBudget { get; set; }
}

public sealed class AnthropicOptions : ProviderOptions
{
    public AnthropicOptions()
    {
        Model = "claude-haiku-5-5";
        RequestsPerMinute = 50;
        MaxOutputTokens["Interpret"] = 1024;
        MaxOutputTokens["Explain"] = 1024;
    }

    /// <summary>"disabled" (default; Haiku 5.5 accepts it at effort ≤ high) | "adaptive".</summary>
    public string Thinking { get; set; } = "disabled";

    /// <summary>output_config.effort: "low" | "medium" | "high"; null = model default (medium on Haiku 5.5).</summary>
    public string? Effort { get; set; } = "low";
}

public sealed class OpenAiOptions : ProviderOptions
{
    public OpenAiOptions()
    {
        Model = "gpt-4.1-mini";
        RequestsPerMinute = 50;
        Temperature = 0;
        MaxOutputTokens["Interpret"] = 512;
        MaxOutputTokens["Explain"] = 900;
    }

    /// <summary>Sent only to reasoning models (gpt-5*, o*): "minimal" keeps p50 near 1 s (AI-01, AI-03).</summary>
    public string? ReasoningEffort { get; set; } = "minimal";
}

public sealed class OperationOptions
{
    public int TimeoutMs { get; set; }
    public int MaxOutputTokens { get; set; }
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);
}

public sealed class AiCacheOptions
{
    /// <summary>Off for parse-stability eval runs (every call goes to the provider).</summary>
    public bool Enabled { get; set; } = true;
    public int TtlMinutes { get; set; } = 1440;
}

public sealed class AiBudgetOptions
{
    /// <summary>
    /// Interpretation first: the last this-many permits of each provider minute window are kept for "interpret"
    /// ("explain" stops when only this many are left), so a busy minute degrades explanations (templates) before
    /// interpretation (AI-56). 0 = no priority.
    /// </summary>
    public int ExplainReserve { get; set; } = 3;

    /// <summary>Estimated paid-tier spend per UTC day (SY-08). 0 = unlimited.</summary>
    public decimal DailyCostCapUsd { get; set; } = 0.50m;

    /// <summary>After a provider 429 the LLM is skipped for Retry-After (or this) seconds: no added latency (AI-56).</summary>
    public int CooldownSeconds { get; set; } = 30;
    public int MaxCooldownSeconds { get; set; } = 300;
}

public sealed class ModelPrice
{
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
}
