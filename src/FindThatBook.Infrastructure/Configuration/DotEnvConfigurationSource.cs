using Microsoft.Extensions.Configuration;

namespace FindThatBook.Infrastructure.Configuration;

/// <summary>Configuration from a <c>.env</c> file (optional: a missing file adds nothing). Never sets process environment variables.</summary>
public sealed class DotEnvConfigurationSource : IConfigurationSource
{
    public required string Path { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DotEnvConfigurationProvider(Path);
}

public sealed class DotEnvConfigurationProvider(string path) : ConfigurationProvider
{
    public string Path { get; } = path;

    public override void Load()
    {
        Data = File.Exists(Path)
            ? WellKnownEnvironmentNames.Map(DotEnvFile.Parse(File.ReadAllText(Path)))
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    // Never print values: the default ToString lists only the type; keep it that way, plus the file name.
    public override string ToString() => $"{nameof(DotEnvConfigurationProvider)} ({System.IO.Path.GetFileName(Path)})";
}

/// <summary>
/// The provider-SDK names (GEMINI_API_KEY, GOOGLE_API_KEY, ANTHROPIC_API_KEY, OPENAI_API_KEY) read from the process
/// environment. Sits just below the plain environment-variable source, so <c>AI__Gemini__ApiKey</c> still wins.
/// </summary>
public sealed class EnvironmentAliasConfigurationSource : IConfigurationSource
{
    /// <summary>For tests: a fake environment. Null reads the real process environment.</summary>
    public IReadOnlyDictionary<string, string>? Variables { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new EnvironmentAliasConfigurationProvider(Variables);
}

public sealed class EnvironmentAliasConfigurationProvider(IReadOnlyDictionary<string, string>? variables) : ConfigurationProvider
{
    public override void Load()
    {
        var entries = WellKnownEnvironmentNames.Aliases
            .Select(a => (a.Name, Value: variables is null ? Environment.GetEnvironmentVariable(a.Name) : variables.GetValueOrDefault(a.Name)))
            .Where(e => e.Value is not null)
            .Select(e => new KeyValuePair<string, string>(e.Name, e.Value!));
        Data = WellKnownEnvironmentNames.Map(entries);
    }

    public override string ToString() => nameof(EnvironmentAliasConfigurationProvider);
}
