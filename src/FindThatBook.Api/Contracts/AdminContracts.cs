using System.ComponentModel.DataAnnotations;
using FindThatBook.Infrastructure.Ai;

namespace FindThatBook.Api.Contracts;

/// <summary>
/// Development-only AI settings. Changes live in memory and are lost on restart; .env is never rewritten.
/// Keys are never returned: only whether one is set, where it came from and a short fingerprint.
/// </summary>
public sealed record AdminAiStatus
{
    public required string ActiveProvider { get; init; }

    /// <summary>"auto" (first provider with a key) | "explicit".</summary>
    public required string ProviderSelection { get; init; }
    public required bool AiConfigured { get; init; }
    public required IReadOnlyList<AdminProviderStatus> Providers { get; init; }
    public required AdminJudgeStatus Judge { get; init; }

    /// <summary>Where runtime changes are kept ("memory": lost on restart).</summary>
    public required string Persistence { get; init; }

    // Explicit field-by-field copy: nothing reaches the wire unless it is listed here.
    internal static AdminAiStatus From(AiStatus s) => new()
    {
        ActiveProvider = s.ActiveProvider,
        ProviderSelection = s.ProviderSelection,
        AiConfigured = s.AiConfigured,
        Providers = [.. s.Providers.Select(p => new AdminProviderStatus
        {
            Name = p.Name, Model = p.Model, Active = p.Active, Configured = p.Configured,
            KeySource = p.KeySource, KeyFingerprint = p.KeyFingerprint,
        })],
        Judge = new AdminJudgeStatus
        {
            Enabled = s.Judge.Enabled, Model = s.Judge.Model, Configured = s.Judge.Configured,
            KeySource = s.Judge.KeySource, KeyFingerprint = s.Judge.KeyFingerprint, Threshold = s.Judge.Threshold,
        },
        Persistence = s.Persistence,
    };
}

public sealed record AdminProviderStatus
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public required bool Active { get; init; }
    public required bool Configured { get; init; }

    /// <summary>runtime, environment, dotenv, user_secrets, appsettings or none.</summary>
    public required string KeySource { get; init; }

    /// <summary>"sha256:" + 8 hex characters of the key's hash, or null. Lets you tell keys apart without seeing them.</summary>
    public required string? KeyFingerprint { get; init; }
}

public sealed record AdminJudgeStatus
{
    public required bool Enabled { get; init; }
    public required string Model { get; init; }
    public required bool Configured { get; init; }
    public required string KeySource { get; init; }
    public required string? KeyFingerprint { get; init; }
    public required double Threshold { get; init; }
}

/// <summary>Any subset of the settings; omitted fields stay as they are.</summary>
public sealed record AdminAiUpdate
{
    [AllowedValues("auto", "gemini", "anthropic", "openai", "none", null)]
    public string? Provider { get; init; }

    /// <summary>Provider name → model id, e.g. {"gemini": "gemini-3.5-flash-lite"}.</summary>
    public IReadOnlyDictionary<string, string>? Models { get; init; }
    public bool? JudgeEnabled { get; init; }
}

public sealed record AdminSetKey
{
    /// <summary>Held in memory only, never returned or logged.</summary>
    [Required, StringLength(512, MinimumLength = 8)]
    public string? ApiKey { get; init; }
}

public sealed record AdminProbeResult
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required bool Ok { get; init; }
    public required string Status { get; init; }
    public required long ElapsedMs { get; init; }
    public required long? InputTokens { get; init; }
    public required long? OutputTokens { get; init; }
    public required string? Detail { get; init; }

    internal static AdminProbeResult From(AiProbeResult r) => new()
    {
        Provider = r.Provider, Model = r.Model, Ok = r.Ok, Status = r.Status, ElapsedMs = r.ElapsedMs,
        InputTokens = r.InputTokens, OutputTokens = r.OutputTokens, Detail = r.Detail,
    };
}
