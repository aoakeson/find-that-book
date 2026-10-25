using System.Collections.Frozen;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

/// <summary>Topic words in search form, shared by the ranker and the retrieval ladder so both read the topic the same way.</summary>
internal static class TopicTerms
{
    /// <summary>
    /// The topics as folded content-word lists, one per phrase, in request order: an interpreter subject heading
    /// ("french revolution", "paris (france)") or a phrase the user typed ("bears", "wolves"). A phrase's words belong
    /// together; different phrases do not.
    /// </summary>
    public static List<IReadOnlyList<string>> Phrases(TopicRequest topic)
        => topic.Terms.Select(t => (IReadOnlyList<string>)TextFolding.Content(TextFolding.Tokens(t)).Distinct().ToList())
            .Where(p => p.Count > 0)
            .ToList();

    /// <summary>
    /// The topic as named to the user: the interpreter's first (most specific) heading with a content word, or the
    /// phrases the user typed; "" when there is none.
    /// </summary>
    public static string Label(TopicRequest topic)
    {
        var terms = topic.Terms.Select(t => t.Trim()).Where(t => TextFolding.Content(TextFolding.Tokens(t)).Count > 0).ToList();
        if (terms.Count == 0) return "";
        return topic.FromInterpreter ? terms[0] : string.Join(", ", terms);
    }

    /// <summary>The interpreter's first heading with a content word (the one its subject search uses), else null.</summary>
    public static string? FirstSubject(TopicRequest topic)
        => topic.Terms.FirstOrDefault(t => TextFolding.Content(TextFolding.Tokens(t)).Count > 0);

    /// <summary>Folded words of a subject heading, with their possible singular forms.</summary>
    public static HashSet<string> HeadingForms(string heading)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in TextFolding.Tokens(heading)) forms.UnionWith(Forms(word));
        return forms;
    }

    /// <summary>A topic word is in a heading when one of its forms is ("foxes" ↔ "Fox", "bear" ↔ "Bears").</summary>
    public static bool InHeading(string word, HashSet<string> headingForms) => Forms(word).Any(headingForms.Contains);

    private static readonly FrozenSet<string> NoPlural = new[] { "news", "series", "species", "means" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The word and the singulars it may be (conservative English rules, whole words only): -ies → -y (stories → story),
    /// -ves → -f / -fe (wolves → wolf, knives → knife), -es after s/x/z/ch/sh (foxes → fox), -s (bears → bear; not
    /// after ss/us/is: glass, campus, paris). Both sides of a comparison are expanded, so singular and plural meet.
    /// </summary>
    public static IEnumerable<string> Forms(string word)
    {
        yield return word;
        if (word.Length <= 3 || NoPlural.Contains(word)) yield break;
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) yield return word[..^3] + "y";
        if (word.Length > 4 && word.EndsWith("ves", StringComparison.Ordinal))
        {
            yield return word[..^3] + "f";
            yield return word[..^3] + "fe";
        }
        if (word.EndsWith("es", StringComparison.Ordinal))
        {
            var stem = word[..^2];
            if (stem.EndsWith('s') || stem.EndsWith('x') || stem.EndsWith('z') || stem.EndsWith("ch", StringComparison.Ordinal)
                || stem.EndsWith("sh", StringComparison.Ordinal))
                yield return stem;
        }
        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal)
            && !word.EndsWith("is", StringComparison.Ordinal))
            yield return word[..^1];
    }
}
