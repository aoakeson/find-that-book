using System.Text.RegularExpressions;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

/// <summary>Contributor credits on Open Library: "Charles Dixon (Adapter)" and the library form "Kemble, E. W., ill.".</summary>
internal static partial class ContributorRoles
{
    /// <summary>"Charles Dixon (Adapter)" → {"charles dixon": "adapter"}; MARC "Kemble, E. W. 1861-1933, ill." → "ill". First role per name wins.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IReadOnlyList<string> contributors)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in contributors)
        {
            string name, role;
            var m = Parenthesised().Match(c);
            if (m.Success)
            {
                name = m.Groups[1].Value;
                role = m.Groups[2].Value.ToLowerInvariant();
            }
            else
            {
                m = MarcSuffix().Match(c);
                if (!m.Success) continue;
                name = m.Groups[1].Value;
                role = m.Groups[2].Value.ToLowerInvariant();
            }
            var key = PersonNames.Key(name);
            if (key.Length > 0 && !IsPlaceholderName(name)) roles.TryAdd(key, role);
        }
        return roles;
    }

    /// <summary>A role that makes a credited person a contributor rather than an author (never one containing "author").</summary>
    public static bool IsContributorRole(string? role)
        => !string.IsNullOrEmpty(role) && !role.Contains("author", StringComparison.Ordinal) && RoleWords().IsMatch(role);

    /// <summary>
    /// A credit whose "name" is only a role or placeholder word ("Author (Illustrator)", "Dramatization (Narrator)",
    /// "Various", "Unknown"): Open Library data noise, never shown as a person.
    /// </summary>
    public static bool IsPlaceholderName(string name)
    {
        var words = PersonNames.Tokens(name);
        return words.Count == 0 || words.All(PlaceholderWords.Contains);
    }

    private static readonly HashSet<string> PlaceholderWords = new(StringComparer.Ordinal)
    {
        "author", "authors", "dramatization", "dramatisation", "various", "unknown", "anonymous", "anon", "illustrator",
        "illustrators", "illustrated", "editor", "editors", "translator", "translators", "narrator", "narrators", "reader",
        "readers", "contributor", "contributors", "others", "other", "et", "al", "staff", "full", "cast", "n", "a", "na",
        "none", "not", "available", "introduction", "foreword", "afterword", "preface", "adapter", "adaptation", "cover",
        "artist", "photographer", "compiler", "performer", "performers", "ensemble", "company",
    };

    /// <summary>Audiobook performer roles (narrator, reader, performer): never authorship roles.</summary>
    public static bool IsPerformer(string role)
        => role.Contains("narrat", StringComparison.Ordinal) || role.Contains("reader", StringComparison.Ordinal)
           || role.Contains("read by", StringComparison.Ordinal) || role.Contains("perform", StringComparison.Ordinal);

    [GeneratedRegex(@"^(.*?)\s*\(([^)]*)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Parenthesised();

    [GeneratedRegex(@"^(.*?),\s*(ill|ed|tr|edt|trl|illus|editor|translator|illustrator)\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarcSuffix();

    [GeneratedRegex(@"adapt|illustr|\bill\b|edit|\bed\b|translat|\btr\b|narrat|reader|read by|introduc|foreword|afterword|preface|notes|compil|annotat|commentar|abridg|design|photograph|artist|cover|contributor|retold|selected|transcri", RegexOptions.CultureInvariant)]
    private static partial Regex RoleWords();
}
