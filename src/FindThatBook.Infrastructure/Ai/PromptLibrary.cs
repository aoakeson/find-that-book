using System.Collections.Concurrent;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Versioned prompts embedded from <c>**/Prompts/{id}.md</c>. Any wording change is a new id, so eval
/// numbers and cache entries trace to one exact prompt.
/// </summary>
public static class PromptLibrary
{
    private static readonly ConcurrentDictionary<string, string> Loaded = new(StringComparer.Ordinal);

    /// <summary>The prompt text (trimmed) for an id such as "interpret.v3"; throws if no embedded file has that name.</summary>
    public static string Get(string id) => Loaded.GetOrAdd(id, Load);

    private static string Load(string id)
    {
        var assembly = typeof(PromptLibrary).Assembly;
        var suffix = ".Prompts." + id + ".md";
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Prompt '{id}' is not embedded (expected a Prompts/{id}.md file).");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
