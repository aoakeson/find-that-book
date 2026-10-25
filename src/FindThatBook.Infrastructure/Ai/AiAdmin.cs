using System.Text.Json;
using FindThatBook.Core.Search;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// The Development admin surface over <see cref="AiSettingsStore"/>. Keys are write-only: status shows only whether a
/// key is set, its source and fingerprint. Invalid input throws <see cref="ArgumentException"/> (the API maps it to 400).
/// </summary>
public sealed class AiAdmin(AiSettingsStore settings, ProviderHealth health, StructuredLlm llm, SearchOptions search) : IAiAdmin
{
    public const string Persistence = "memory (runtime changes are lost on restart)";

    public AiStatus GetStatus()
    {
        var active = settings.ActiveProvider;
        var providers = AiProviders.All.Select(p =>
        {
            var key = settings.KeyFor(p);
            return new ProviderStatus(p, settings.ModelFor(p), p == active, key.IsSet, key.Source, key.Fingerprint);
        }).ToList();

        var activeTarget = settings.Target(active);
        var aiConfigured = activeTarget is not null && health.DisabledReason(activeTarget) is null;

        var judgeKey = settings.KeyFor(AiSettingsStore.JudgeKeyName);
        var judge = new JudgeSettingsStatus(settings.JudgeEnabled, settings.JudgeOptions.Model, judgeKey.IsSet,
            judgeKey.Source, judgeKey.Fingerprint, search.JudgeThreshold);
        return new AiStatus(active, aiConfigured, providers, judge, Persistence, settings.ProviderSelection);
    }

    public AiStatus Update(AiSettingsUpdate update)
    {
        // Validate everything before applying anything, so a bad field leaves the settings untouched.
        var provider = update.Provider is null ? null : AiProviders.Normalize(update.Provider)
            ?? throw new ArgumentException($"Unknown provider '{update.Provider}'.", nameof(update));
        var models = new List<(string Provider, string Model)>();
        foreach (var (name, model) in update.Models ?? new Dictionary<string, string>())
        {
            var p = AiProviders.Normalize(name);
            if (p is null or AiProviders.None or AiProviders.Auto) throw new ArgumentException($"Unknown provider '{name}'.", nameof(update));
            if (!ModelIds.IsValid(model)) throw new ArgumentException($"Model id for '{p}' is missing or invalid.", nameof(update));
            models.Add((p, model));
        }

        if (provider is not null) settings.SetProvider(provider);
        foreach (var (p, model) in models) settings.SetModel(p, model);
        if (update.JudgeEnabled is { } enabled) settings.SetJudgeEnabled(enabled);
        return GetStatus();
    }

    public AiStatus SetKey(string provider, string apiKey)
    {
        settings.SetKey(provider, apiKey);
        return GetStatus();
    }

    public AiStatus ClearKey(string provider)
    {
        settings.ClearKey(provider);
        return GetStatus();
    }

    /// <summary>A real interpret call (prompt v3, "tolkien hobbit") through the adapter, cache bypassed; counts toward the budget.</summary>
    public async Task<AiProbeResult> ProbeAsync(string provider, CancellationToken ct)
    {
        var p = AiProviders.Normalize(provider);
        if (p is null or AiProviders.None or AiProviders.Auto) throw new ArgumentException($"Unknown provider '{provider}'.", nameof(provider));
        var call = LlmQueryInterpreter.BuildCall("tolkien hobbit", settings.Options);
        var result = await llm.CallAsync<InterpretOutput>(call, p, useCache: false, ct);
        var c = result.Call;
        var detail = result.Value is { } v ? $"title={v.Title ?? "null"}; author={v.Author ?? "null"}" : c.Detail;
        return new AiProbeResult(c.Provider, c.Model, c.Succeeded, JsonNamingPolicy.SnakeCaseLower.ConvertName(c.Status.ToString()),
            c.ElapsedMs, c.InputTokens, c.OutputTokens, detail);
    }
}
