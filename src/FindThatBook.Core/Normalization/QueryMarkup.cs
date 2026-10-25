using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Core.Normalization;

/// <summary>
/// Pasted-text cleanup before tokenizing (IN-026, IN-027, IN-075): HTML entities decoded once, markdown
/// links keep their text, a bare URL keeps only its human-readable slug, and leftover %XX escapes are decoded once.
/// </summary>
public static partial class QueryMarkup
{
    public static string Clean(string? text)
    {
        var q = WebUtility.HtmlDecode(text ?? "");
        q = MarkdownLink().Replace(q, m => m.Groups[1].Value);
        q = Url().Replace(q, m => " " + UrlWords(m.Value) + " ");
        // IN-027: "pride%20and%20prejudice" survived the framework's decode; decode once more.
        return PercentEscape().IsMatch(q) ? Uri.UnescapeDataString(q) : q;
    }

    /// <summary>
    /// The readable slug of a URL: ".../Left-Hand-Darkness/dp/1857230744/ref=sr_1_1" → "Left Hand Darkness",
    /// ".../book/show/30119.Where_the_Sidewalk_Ends" → "Where the Sidewalk Ends". Host, ids and one-word paths
    /// ("dp", "ebooks") are dropped: a segment counts only when it has at least two all-letter words.
    /// </summary>
    public static string UrlWords(string url)
    {
        var path = Host().Replace(url, "").Split('?')[0];
        var words = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            var decoded = PercentEscape().IsMatch(segment) ? Uri.UnescapeDataString(segment) : segment;
            var parts = SlugSeparator().Split(decoded).Where(IsAllLetters).ToList();
            if (parts.Count >= 2) words.AddRange(parts);
        }
        return string.Join(' ', words);
    }

    private static bool IsAllLetters(string w) => w.Length > 0 && w.EnumerateRunes().All(Rune.IsLetter);

    [GeneratedRegex(@"\[([^\]]+)\]\((?:https?://|www\.)[^)\s]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"^(?:https?://)?[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex Host();

    [GeneratedRegex(@"[-_.+]+")]
    private static partial Regex SlugSeparator();

    [GeneratedRegex(@"%[0-9A-Fa-f]{2}")]
    private static partial Regex PercentEscape();
}
