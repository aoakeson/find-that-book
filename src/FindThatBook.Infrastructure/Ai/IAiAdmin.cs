namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Runtime view and control of provider settings for the Development-only admin API. Changes live in memory only
/// and are lost on restart; keys are never returned, only whether one is set, where it came from and a fingerprint.
/// </summary>
public interface IAiAdmin
{
    AiStatus GetStatus();
    AiStatus Update(AiSettingsUpdate update);
    AiStatus SetKey(string provider, string apiKey);
    AiStatus ClearKey(string provider);

    /// <summary>One tiny structured call through the real adapter, to prove a provider works.</summary>
    Task<AiProbeResult> ProbeAsync(string provider, CancellationToken ct);
}

/// <param name="KeySource">"runtime" | "environment" | "dotenv" | "user_secrets" | "appsettings" | "none".</param>
/// <param name="KeyFingerprint">"sha256:" + first 8 hex chars of the key's hash, or null.</param>
public sealed record ProviderStatus(string Name, string Model, bool Active, bool Configured, string KeySource, string? KeyFingerprint);

public sealed record JudgeSettingsStatus(bool Enabled, string Model, bool Configured, string KeySource, string? KeyFingerprint, double Threshold);

/// <param name="ActiveProvider">The provider in use ("gemini" | "anthropic" | "openai" | "none"), after resolving "auto".</param>
/// <param name="ProviderSelection">"auto" (first provider with a key) | "explicit" (a configured or admin-set name).</param>
public sealed record AiStatus(string ActiveProvider, bool AiConfigured, IReadOnlyList<ProviderStatus> Providers, JudgeSettingsStatus Judge,
    string Persistence, string ProviderSelection = "explicit");

/// <param name="Provider">"auto" | "gemini" | "anthropic" | "openai" | "none".</param>
/// <param name="Models">Provider name → model id.</param>
public sealed record AiSettingsUpdate(string? Provider = null, IReadOnlyDictionary<string, string>? Models = null, bool? JudgeEnabled = null);

public sealed record AiProbeResult(string Provider, string Model, bool Ok, string Status, long ElapsedMs, long? InputTokens, long? OutputTokens, string? Detail);

/// <summary>Estimated paid-tier cost; null for a model missing from the price table (never a fake 0).</summary>
public interface IAiPricing
{
    decimal? Estimate(string model, long? inputTokens, long? outputTokens);
}
