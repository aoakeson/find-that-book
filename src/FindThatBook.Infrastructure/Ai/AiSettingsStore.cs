using FindThatBook.Infrastructure.Judge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Effective provider settings: configuration plus in-memory runtime overrides from the admin API (lost on restart,
/// ADR 0007). Runtime values win over every configuration source.
/// </summary>
public sealed class AiSettingsStore(IOptions<AiOptions> options, IOptions<JudgeOptions> judge, IConfiguration configuration)
{
    public const string JudgeKeyName = "judge";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private string? _provider;
    private bool? _judgeEnabled;

    public AiOptions Options => options.Value;
    public JudgeOptions JudgeOptions => judge.Value;

    /// <summary>The provider calls go to: "gemini" | "anthropic" | "openai" | "none" (the "auto" setting resolved).</summary>
    public string ActiveProvider
    {
        get
        {
            var setting = ProviderSetting;
            return setting == AiProviders.Auto
                ? AiProviders.All.FirstOrDefault(p => KeyFor(p).IsSet) ?? AiProviders.None
                : setting;
        }
    }

    /// <summary>"auto" | "explicit": whether <see cref="ActiveProvider"/> was chosen by key availability.</summary>
    public string ProviderSelection => ProviderSetting == AiProviders.Auto ? "auto" : "explicit";

    /// <summary>The raw setting: runtime override, else configuration ("auto" | "none" | a provider).</summary>
    private string ProviderSetting
    {
        get
        {
            lock (_gate) return _provider ?? AiProviders.Normalize(options.Value.Provider) ?? AiProviders.None;
        }
    }

    public string ModelFor(string provider)
    {
        lock (_gate)
            return _models.TryGetValue(provider, out var m) ? m : options.Value.For(provider)?.Model ?? "";
    }

    public ResolvedKey KeyFor(string provider)
    {
        lock (_gate)
            if (_keys.TryGetValue(provider, out var runtime)) return new ResolvedKey(runtime, KeySources.Runtime);
        if (provider == JudgeKeyName)
        {
            var own = KeySources.Find(configuration, "Judge:ApiKey");
            if (!own.IsSet && judge.Value.ApiKey is { } configured) own = new ResolvedKey(configured, KeySources.AppSettings);
            return own.IsSet ? own : KeyFor(AiProviders.OpenAi);
        }
        var section = provider switch
        {
            AiProviders.Gemini => "Gemini",
            AiProviders.Anthropic => "Anthropic",
            AiProviders.OpenAi => "OpenAI",
            _ => null,
        };
        if (section is null) return ResolvedKey.None;
        var found = KeySources.Find(configuration, $"{AiOptions.Section}:{section}:ApiKey");
        // Options configured in code (tests, the eval tool) rather than through IConfiguration.
        return found.IsSet || options.Value.For(provider)?.ApiKey is not { } inCode ? found : new ResolvedKey(inCode, KeySources.AppSettings);
    }

    /// <summary>What a call to <paramref name="provider"/> would use, or null when it is "none" or has no key.</summary>
    public ProviderTarget? Target(string provider)
    {
        var p = options.Value.For(provider);
        if (p is null) return null;
        var key = KeyFor(provider);
        return key.IsSet ? new ProviderTarget(provider, ModelFor(provider), key, p) : null;
    }

    public bool JudgeEnabled
    {
        get
        {
            lock (_gate) return _judgeEnabled ?? judge.Value.Enabled;
        }
    }

    public void SetProvider(string provider)
    {
        var p = AiProviders.Normalize(provider) ?? throw new ArgumentException($"Unknown provider '{provider}'.", nameof(provider));
        lock (_gate) _provider = p;
    }

    public void SetModel(string provider, string model)
    {
        var p = RequireChatProvider(provider);
        if (!ModelIds.IsValid(model)) throw new ArgumentException("Model id is missing or invalid.", nameof(model));
        lock (_gate) _models[p] = model.Trim();
    }

    public void SetJudgeEnabled(bool enabled)
    {
        lock (_gate) _judgeEnabled = enabled;
    }

    /// <param name="provider">A chat provider or "judge".</param>
    public void SetKey(string provider, string apiKey)
    {
        var p = RequireKeyName(provider);
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 512 || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("API key is empty, too long or contains whitespace.", nameof(apiKey));
        lock (_gate) _keys[p] = key;
    }

    /// <summary>Drops the runtime key; the configured value (if any) applies again.</summary>
    public void ClearKey(string provider)
    {
        var p = RequireKeyName(provider);
        lock (_gate) _keys.Remove(p);
    }

    private static string RequireChatProvider(string provider) =>
        AiProviders.Normalize(provider) is { } p && p is not (AiProviders.None or AiProviders.Auto)
            ? p
            : throw new ArgumentException($"Unknown provider '{provider}'.", nameof(provider));

    private static string RequireKeyName(string provider) =>
        string.Equals(provider?.Trim(), JudgeKeyName, StringComparison.OrdinalIgnoreCase) ? JudgeKeyName : RequireChatProvider(provider!);
}

/// <summary>One provider, model and key to call. Never prints the key.</summary>
public sealed class ProviderTarget(string provider, string model, ResolvedKey key, ProviderOptions options)
{
    public string Provider { get; } = provider;
    public string Model { get; } = model;
    public ResolvedKey Key { get; } = key;
    public ProviderOptions Options { get; } = options;

    /// <summary>Cache/health identity: provider, model and key fingerprint (never the key).</summary>
    public string Identity => $"{Provider}|{Model}|{Key.Fingerprint}";

    public override string ToString() => $"{Provider}/{Model} ({Key})";
}
