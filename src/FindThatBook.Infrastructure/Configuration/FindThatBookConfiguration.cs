using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace FindThatBook.Infrastructure.Configuration;

/// <summary>
/// One configuration layout for the API and the tools (the eval must run exactly what the API runs).
/// Precedence, lowest to highest:
/// appsettings.json &lt; appsettings.{Environment}.json &lt; user secrets (Development) &lt; repo-root .env
/// &lt; well-known environment names (GEMINI_API_KEY…) &lt; environment variables (AI__Gemini__ApiKey…)
/// &lt; command line. Runtime admin changes (Development only, in memory) sit above all of these.
/// </summary>
public static class FindThatBookConfiguration
{
    public const string SolutionFileName = "FindThatBook.slnx";
    public const string DotEnvFileName = ".env";

    /// <summary>The API project's UserSecretsId, so tools read the same secrets in Development.</summary>
    public const string ApiUserSecretsId = "find-that-book-api";

    /// <summary>
    /// Off switches, read before the sources are added, for runs that must not see a developer's keys: the API tests
    /// and the eval's keyless replay. Both default to true.
    /// </summary>
    public const string DotEnvEnabledKey = "DotEnv:Enabled";
    public const string EnvironmentAliasesEnabledKey = "EnvironmentAliases:Enabled";

    /// <summary>
    /// Adds the .env file and the well-known environment names to a builder that already has the standard sources
    /// (WebApplicationBuilder.Configuration): both go just below the plain environment-variable source.
    /// </summary>
    /// <param name="contentRootPath">
    /// Where the walk up to the folder holding FindThatBook.slnx starts. Only the content root is searched, so a
    /// published app (no solution file above it) reads no .env and uses environment variables only.
    /// </param>
    public static IConfigurationManager AddFindThatBookSources(this IConfigurationManager configuration, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var dotEnvPath = configuration.GetValue(DotEnvEnabledKey, true) ? FindDotEnvFile(contentRootPath) : null;
        var aliases = configuration.GetValue(EnvironmentAliasesEnabledKey, true)
            ? new EnvironmentAliasConfigurationSource()
            : null;
        InsertSources(configuration.Sources, dotEnvPath, aliases);
        return configuration;
    }

    /// <summary>
    /// Places the .env source and the alias source directly below the last unprefixed environment-variable source
    /// (or below the command line, or at the end, when there is none). Public so the precedence can be tested.
    /// </summary>
    public static void InsertSources(IList<IConfigurationSource> sources, string? dotEnvPath, EnvironmentAliasConfigurationSource? aliases)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var index = LastIndex(sources, s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        if (index < 0) index = LastIndex(sources, s => s is CommandLineConfigurationSource);
        if (index < 0) index = sources.Count;

        if (aliases is not null) sources.Insert(index, aliases);
        if (dotEnvPath is not null) sources.Insert(index, new DotEnvConfigurationSource { Path = dotEnvPath });
    }

    /// <summary>
    /// Builds the API's configuration for a console tool (the eval): the API project's appsettings, user secrets in
    /// Development, .env, environment variables and <paramref name="args"/>, in the API's precedence order.
    /// </summary>
    /// <param name="environmentName">Defaults to DOTNET_ENVIRONMENT, then ASPNETCORE_ENVIRONMENT, then Production.</param>
    /// <param name="repositoryRoot">Defaults to the folder holding FindThatBook.slnx above the current or base directory.</param>
    public static IConfigurationRoot BuildForTool(string[] args, string? environmentName = null, string? repositoryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var root = repositoryRoot
            ?? FindRepositoryRoot(Directory.GetCurrentDirectory(), AppContext.BaseDirectory)
            ?? throw new InvalidOperationException($"Could not find {SolutionFileName} above the current directory.");
        var environment = environmentName
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configuration = new ConfigurationManager();
        configuration.SetBasePath(Path.Combine(root, "src", "FindThatBook.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);
        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            configuration.AddUserSecrets(ApiUserSecretsId);
        configuration.AddEnvironmentVariables().AddCommandLine(args);
        configuration.AddFindThatBookSources(root);
        return configuration;
    }

    /// <summary>The first directory, walking up from each start in turn, that contains FindThatBook.slnx.</summary>
    public static string? FindRepositoryRoot(params string?[] startDirectories)
    {
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrEmpty(start)) continue;
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, SolutionFileName))) return dir.FullName;
            }
        }
        return null;
    }

    /// <summary>The repo-root .env path (whether or not the file exists), or null when the repo root can't be found.</summary>
    public static string? FindDotEnvFile(params string?[] startDirectories)
        => FindRepositoryRoot(startDirectories) is { } root ? Path.Combine(root, DotEnvFileName) : null;

    private static int LastIndex(IList<IConfigurationSource> sources, Func<IConfigurationSource, bool> match)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (match(sources[i])) return i;
        }
        return -1;
    }
}
