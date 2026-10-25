using System.Text.RegularExpressions;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Infrastructure.Suggest;

/// <summary>
/// The keys the suggest index stores and the key a typed prefix becomes, all built from the shared
/// <see cref="TextFolding.Tokens"/> so suggest folds exactly like search (accents, case, curly quotes, "&amp;" → "and",
/// elisions; CJK and Cyrillic letters kept, DE-07).
/// Unlike the search title key, suggest keys keep stop words ("lord of th" must still match while typing) and drop
/// only a leading English article, so "hob" is a whole-title match for The Hobbit.
/// </summary>
internal static partial class SuggestText
{
    static readonly HashSet<string> Articles = ["the", "a", "an"];

    /// <summary>The key a typed prefix is matched with: folded tokens, a leading article dropped when more follows.</summary>
    public static string PrefixKey(string? prefix)
    {
        var tokens = TextFolding.Tokens(prefix);
        var start = tokens.Count > 1 && Articles.Contains(tokens[0]) ? 1 : 0;
        return string.Join(' ', tokens.Skip(start));
    }

    /// <summary>
    /// The whole-title keys of one title: its words as written, plus the number↔word aliases
    /// ("Nineteen Eighty-Four" → "1984"; "20,000 Leagues…" → "twenty thousand leagues…"). Bracketed and parenthesised
    /// groups are dropped ("Código civil (1984)" is not a "1984" title). Distinct, first = as written.
    /// </summary>
    public static IReadOnlyList<string[]> TitleForms(string title)
    {
        string[] tokens = [.. TextFolding.Tokens(Brackets().Replace(title, " "))];
        if (tokens.Length == 0) tokens = [.. TextFolding.Tokens(title)];
        if (tokens.Length > 1 && Articles.Contains(tokens[0])) tokens = tokens[1..];
        if (tokens.Length == 0) return [];

        var forms = new List<string[]> { tokens };
        AddDistinct(forms, [.. Numerals.ToDigits(tokens)]);
        AddDistinct(forms, NumberWords.Expand(tokens));
        return forms;
    }

    /// <summary>
    /// Char offsets into <c>string.Join(' ', form)</c> where a word-prefix key starts: every word after the first that
    /// is not a stop word. The key runs to the end of the title, so "chamber of sec" matches word-aligned.
    /// </summary>
    public static IEnumerable<int> WordStarts(string[] form)
    {
        var offset = 0;
        for (var i = 0; i < form.Length; i++)
        {
            if (i > 0 && !Vocabulary.Stop.Contains(form[i])) yield return offset;
            offset += form[i].Length + 1;
        }
    }

    /// <summary>
    /// Author keys: the search name key ("jane austen", initials joined: "jrr tolkien") and the name from each
    /// non-initial word on ("austen", "cervantes saavedra"), so a surname prefix finds the author.
    /// </summary>
    public static IReadOnlyList<string> AuthorKeys(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return [];
        var tokens = PersonNames.Tokens(author);
        var keys = new List<string>();
        var nameKey = PersonNames.Key(author);
        if (nameKey.Length > 0) keys.Add(nameKey);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Length < 2) continue;
            var key = string.Join(' ', tokens.Skip(i));
            if (!keys.Contains(key)) keys.Add(key);
        }
        return keys;
    }

    /// <summary>
    /// Build-time dedupe identity: the search title key (main title, stop words out, numerals as digits) plus the
    /// author name key, so "Pride &amp; Prejudice" and "Pride and Prejudice: A Novel" by Jane Austen are one work.
    /// </summary>
    public static string DedupeKey(string title, string? author) => $"{TitleText.Key(title)}\u0001{PersonNames.Key(author ?? "")}";

    static void AddDistinct(List<string[]> forms, string[] form)
    {
        if (!forms.Any(f => f.AsSpan().SequenceEqual(form))) forms.Add(form);
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Brackets();
}
