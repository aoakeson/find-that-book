using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Core.Explanations;

/// <summary>
/// Text helpers shared by the facts builder, the template and the grounding check. Kept local (not the query
/// normalizer) because the check must compare LLM output with catalog strings the same way it builds its vocabulary.
/// </summary>
internal static partial class ExplanationText
{
    /// <summary>Lowercase, compatibility-decompose and drop combining marks (AI-28: "García" = "garcia").</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes()) AppendFolded(sb, rune);
        return sb.ToString();
    }

    /// <summary>Folded letters and digits only, with the source index of each kept character.</summary>
    public static (string Skeleton, int[] Source) Skeleton(string text)
    {
        var sb = new StringBuilder(text.Length);
        var source = new List<int>(text.Length);
        var folded = new StringBuilder(4);
        var i = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            folded.Clear();
            AppendFolded(folded, rune);
            foreach (var r in folded.ToString().EnumerateRunes())
            {
                if (!Rune.IsLetterOrDigit(r)) continue;
                sb.Append(r.ToString());
                for (var k = 0; k < r.Utf16SequenceLength; k++) source.Add(i);
            }
            i += rune.Utf16SequenceLength;
        }
        return (sb.ToString(), [.. source]);
    }

    /// <summary>Folded word tokens: runs of letters and digits (apostrophes and hyphens split words).</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        var folded = Fold(text);
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var rune in folded.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) sb.Append(rune.ToString());
            else if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    public static bool IsWordRune(Rune r) => Rune.IsLetterOrDigit(r) || Rune.GetUnicodeCategory(r) == UnicodeCategory.NonSpacingMark;

    /// <summary>Same letters and digits, ignoring case, accents, spacing and punctuation.</summary>
    public static bool SameText(string? a, string? b) => Skeleton(a ?? "").Skeleton == Skeleton(b ?? "").Skeleton;

    /// <summary>Cuts at the last space before <paramref name="max"/> characters (AI-34 caps keep the batch small).</summary>
    public static string Cap(string text, int max)
    {
        text = text.Trim();
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max);
        return (cut > max / 2 ? text[..cut] : text[..max]).TrimEnd(' ', ',', ';', ':', '-');
    }

    /// <summary>
    /// A display name from a catalog credit (AI-37): drops parentheticals and life dates and un-inverts
    /// "Eliot, T. S." → "T. S. Eliot". "Kemble, E. W. 1861-1933" → "E. W. Kemble".
    /// </summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = Parenthetical().Replace(name, " ");
        s = LifeDates().Replace(s, " ");
        s = Spaces().Replace(s, " ").Trim().TrimEnd(',', ';', ':', ' ');
        var comma = s.IndexOf(',');
        if (comma > 0 && comma == s.LastIndexOf(','))
        {
            var last = s[..comma].Trim();
            var first = s[(comma + 1)..].Trim();
            if (first.Length > 0 && !NameSuffix().IsMatch(first)) s = $"{first} {last}";
        }
        return Spaces().Replace(s, " ").Trim();
    }

    /// <summary>The family name used for a short mention ("J.R.R. Tolkien" → "Tolkien", "Martin Luther King Jr." → "King").</summary>
    public static string Surname(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var p = parts[i].Trim(',', '.');
            if (p.Length > 1 && !NameSuffix().IsMatch(p)) return parts[i].TrimEnd(',');
        }
        return name;
    }

    /// <summary>True when the text has a letter outside the Latin script (三体, Война и мир).</summary>
    public static bool HasNonLatinLetter(string text)
    {
        foreach (var r in text.EnumerateRunes())
            if (Rune.IsLetter(r) && !IsLatin(r)) return true;
        return false;
    }

    public static bool IsLatinOnly(string text)
    {
        var any = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(r)) continue;
            if (!IsLatin(r)) return false;
            any = true;
        }
        return any;
    }

    private static bool IsLatin(Rune r) => r.Value <= 0x024F || r.Value is >= 0x1E00 and <= 0x1EFF;

    private static void AppendFolded(StringBuilder sb, Rune rune)
    {
        var decomposed = rune.ToString().Normalize(NormalizationForm.FormKD);
        foreach (var r in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(r) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(Rune.ToLowerInvariant(r).ToString());
        }
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\b(b\.|d\.|fl\.|ca\.)?\s*\d{3,4}\??(\s*-\s*(\d{2,4}\??)?)?")]
    private static partial Regex LifeDates();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(jr|sr|ii|iii|iv|v)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex NameSuffix();
}

/// <summary>
/// Contributor roles as one lexicon (AI-36): a role is a category ("adapter") with the words that express it, so
/// "adapted by", "adaptor" and "adapter" agree. Unknown labels are dropped from the facts (AI-37).
/// </summary>
internal static class RoleLexicon
{
    public const string Generic = "contributor";

    private static readonly (string Category, string[] Words)[] Categories =
    [
        ("adapter", ["adapter", "adapters", "adaptor", "adaptors", "adapted", "adaptation", "adaptations", "retold", "reteller"]),
        ("illustrator", ["illustrator", "illustrators", "illustrated", "illustration", "illustrations"]),
        ("editor", ["editor", "editors", "edited"]),
        ("translator", ["translator", "translators", "translated", "translation", "translations"]),
        ("narrator", ["narrator", "narrators", "narrated", "reader", "readers"]),
        ("compiler", ["compiler", "compilers", "compiled"]),
        ("annotator", ["annotator", "annotators", "annotated", "annotations"]),
        ("introduction writer", ["introduction", "introductions", "introduced"]),
        ("foreword writer", ["foreword", "forewords"]),
        ("afterword writer", ["afterword", "afterwords"]),
        ("preface writer", ["preface", "prefaces"]),
    ];

    private static readonly HashSet<string> GenericWords = ["contributor", "contributors", "contributed", "contribution", "contributions"];

    private static readonly Dictionary<string, string> CategoryByWord = Categories
        .SelectMany(c => c.Words.Select(w => (w, c.Category)))
        .ToDictionary(x => x.w, x => x.Category, StringComparer.Ordinal);

    /// <summary>The category a folded word expresses, <see cref="Generic"/> for "contributor", or null.</summary>
    public static string? CategoryOfWord(string folded) =>
        CategoryByWord.TryGetValue(folded, out var c) ? c : GenericWords.Contains(folded) ? Generic : null;

    /// <summary>
    /// Maps a catalog role label ("Adapter", "ill.", "edt", "Translated by") to a category. Labels that contain
    /// "author" are not contributor roles (DA-020); labels we do not know are dropped (AI-37) by returning null.
    /// </summary>
    public static string? Parse(string? label)
    {
        if (label is null) return Generic;
        var words = ExplanationText.Tokens(label);
        if (words.Count == 0) return Generic;
        if (words.Any(w => w.StartsWith("author", StringComparison.Ordinal))) return null;
        // Whole-word stems: "also credited" contains "edit" but is not an editor credit.
        foreach (var w in words)
        {
            if (w.StartsWith("adapt", StringComparison.Ordinal) || w.StartsWith("retold", StringComparison.Ordinal) || w.StartsWith("retell", StringComparison.Ordinal)) return "adapter";
            if (w.StartsWith("illustr", StringComparison.Ordinal) || w is "ill" or "illus") return "illustrator";
            if ((w.StartsWith("edit", StringComparison.Ordinal) && !w.StartsWith("edition", StringComparison.Ordinal)) || w is "ed" or "eds" or "edt") return "editor";
            if (w.StartsWith("translat", StringComparison.Ordinal) || w is "tr" or "trl" or "trans") return "translator";
            if (w.StartsWith("narrat", StringComparison.Ordinal) || w.StartsWith("reader", StringComparison.Ordinal) || w == "read") return "narrator";
            if (w.StartsWith("compil", StringComparison.Ordinal)) return "compiler";
            if (w.StartsWith("annotat", StringComparison.Ordinal)) return "annotator";
            if (w.StartsWith("introduc", StringComparison.Ordinal)) return "introduction writer";
            if (w.StartsWith("foreword", StringComparison.Ordinal)) return "foreword writer";
            if (w.StartsWith("afterword", StringComparison.Ordinal)) return "afterword writer";
            if (w.StartsWith("preface", StringComparison.Ordinal)) return "preface writer";
            if (w.StartsWith("contribut", StringComparison.Ordinal)) return Generic;
        }
        return null;
    }

    private static readonly HashSet<string> JunkNameWords =
        ["author", "authors", "dramatization", "dramatisation", "various", "anonymous", "unknown", "other", "others", "staff", "cast", "full", "and"];

    /// <summary>
    /// A credit whose "name" is only role or filler words: Open Library has "Author (Illustrator)" on The Hobbit
    /// (OL27482W) and "Dramatization (Narrator)" on Oliver Twist. Such names are never shown.
    /// </summary>
    public static bool IsJunkName(string name)
    {
        var words = ExplanationText.Tokens(name);
        return words.Count == 0 || words.All(w => JunkNameWords.Contains(w) || CategoryOfWord(w) is not null);
    }

    /// <summary>The words that express a category ("illustrator" → illustrator, illustrated, illustrations…).</summary>
    public static IEnumerable<string> WordsFor(string category) =>
        category == Generic ? GenericWords : Categories.FirstOrDefault(c => c.Category == category).Words ?? [];

    /// <summary>"adapter" → "adapters", "foreword writer" → "foreword writers".</summary>
    public static string Plural(string category) => category + "s";
}
