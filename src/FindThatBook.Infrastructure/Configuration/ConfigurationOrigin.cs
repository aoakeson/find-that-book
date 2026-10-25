using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace FindThatBook.Infrastructure.Configuration;

/// <summary>
/// Which layer supplied a key (for the admin status "key_source"), without reading the value out.
/// Returns "command_line" | "environment" | "dotenv" | "user_secrets" | "appsettings" | "other" | "none".
/// </summary>
public static class ConfigurationOrigin
{
    public static string Of(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root) return string.IsNullOrWhiteSpace(configuration[key]) ? "none" : "other";
        foreach (var provider in root.Providers.Reverse())
        {
            if (!provider.TryGet(key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            return provider switch
            {
                CommandLineConfigurationProvider => "command_line",
                EnvironmentVariablesConfigurationProvider or EnvironmentAliasConfigurationProvider => "environment",
                DotEnvConfigurationProvider => "dotenv",
                JsonConfigurationProvider { Source.Path: "secrets.json" } => "user_secrets",
                JsonConfigurationProvider => "appsettings",
                _ => "other",
            };
        }
        return "none";
    }
}
