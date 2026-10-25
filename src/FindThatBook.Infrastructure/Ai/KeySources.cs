using System.Security.Cryptography;
using System.Text;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>An API key and where it came from. <see cref="ToString"/> never shows the key.</summary>
public sealed class ResolvedKey
{
    public static ResolvedKey None { get; } = new(null, KeySources.None);

    public ResolvedKey(string? value, string source)
    {
        Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Source = Value is null ? KeySources.None : source;
        Fingerprint = Value is null ? null : KeySources.Fingerprint(Value);
    }

    /// <summary>The secret. Only the SDK client factory and the judge's Authorization header read it.</summary>
    public string? Value { get; }
    public string Source { get; }
    public string? Fingerprint { get; }
    public bool IsSet => Value is not null;

    public override string ToString() => $"{Source}:{Fingerprint ?? "none"}";
}

/// <summary>Key lookup with its source, in the admin contract's vocabulary (ADR 0007 precedence: later providers win).</summary>
public static class KeySources
{
    public const string Runtime = "runtime";
    public const string Environment = "environment";
    public const string DotEnv = "dotenv";
    public const string UserSecrets = "user_secrets";
    public const string AppSettings = "appsettings";
    public const string None = "none";

    /// <summary>"sha256:" + the first 8 hex characters of the key's SHA-256: enough to tell keys apart, useless to an attacker.</summary>
    public static string Fingerprint(string key) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    /// <summary>
    /// The winning value and the layer that supplied it, classified by the API's loader (<see cref="ConfigurationOrigin"/>).
    /// Its "command_line" counts as environment and "other" (in-memory sources, tests) as appsettings, the two nearest
    /// labels of <see cref="ProviderStatus.KeySource"/>.
    /// </summary>
    public static ResolvedKey Find(IConfiguration configuration, string path)
    {
        var value = configuration[path];
        if (string.IsNullOrWhiteSpace(value)) return ResolvedKey.None;
        var source = ConfigurationOrigin.Of(configuration, path) switch
        {
            "command_line" or "environment" => Environment,
            "dotenv" => DotEnv,
            "user_secrets" => UserSecrets,
            _ => AppSettings,
        };
        return new ResolvedKey(value, source);
    }
}
