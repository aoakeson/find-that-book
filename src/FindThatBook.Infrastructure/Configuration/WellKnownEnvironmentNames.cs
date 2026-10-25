using Microsoft.Extensions.Configuration;

namespace FindThatBook.Infrastructure.Configuration;

/// <summary>
/// Provider SDK conventions accepted as aliases for our configuration keys, in <c>.env</c> and in the process
/// environment. The explicit hierarchical name (<c>AI__Gemini__ApiKey</c>) always beats an alias from the same
/// source; GEMINI_API_KEY beats GOOGLE_API_KEY (we document GEMINI_API_KEY).
/// </summary>
public static class WellKnownEnvironmentNames
{
    /// <summary>Lowest precedence first: a later alias for the same key overrides an earlier one.</summary>
    public static IReadOnlyList<(string Name, string ConfigurationKey)> Aliases { get; } =
    [
        ("GOOGLE_API_KEY", "AI:Gemini:ApiKey"),
        ("GEMINI_API_KEY", "AI:Gemini:ApiKey"),
        ("ANTHROPIC_API_KEY", "AI:Anthropic:ApiKey"),
        ("OPENAI_API_KEY", "AI:OpenAI:ApiKey"),
    ];

    public static bool IsAlias(string name) => Aliases.Any(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    /// <summary><c>AI__Gemini__ApiKey</c> → <c>AI:Gemini:ApiKey</c> (the environment-variable convention).</summary>
    public static string ToConfigurationKey(string name) => name.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);

    /// <summary>
    /// Maps raw name/value pairs to configuration data: aliases first, then explicit names, so an explicit name wins.
    /// Empty values are ignored, so a copied <c>.env.example</c> changes nothing.
    /// </summary>
    public static Dictionary<string, string?> Map(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var list = entries.Where(e => !string.IsNullOrWhiteSpace(e.Value)).ToList();
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, key) in Aliases)
        {
            foreach (var entry in list)
            {
                if (string.Equals(entry.Key, name, StringComparison.Ordinal)) data[key] = entry.Value.Trim();
            }
        }
        foreach (var entry in list)
        {
            if (!IsAlias(entry.Key)) data[ToConfigurationKey(entry.Key)] = entry.Value;
        }
        return data;
    }
}
