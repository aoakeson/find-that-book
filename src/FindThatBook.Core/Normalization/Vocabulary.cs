using System.Collections.Frozen;
using System.Text.RegularExpressions;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Normalization;

/// <summary>Stop, noise, intent, series and ordinal word lists.</summary>
public static partial class Vocabulary
{
    private static readonly FrozenSet<string> StopWords = Set("the a an of and or in on to for by with at from as is");

    private static readonly FrozenSet<string> NoiseWords = Set(
        "illustrated illustrations deluxe edition editions ed hardcover hardback paperback softcover boxed box set annotated " +
        "unabridged abridged kindle ebook epub pdf audiobook audio cd mp3 signed 1st 2nd 3rd collectors collector collectible " +
        "anniversary special vintage classic classics penguin oxford version copy book books novel reprint large print hc pb");

    // Words that ask for a different work than the canonical one. Pure edition words (illustrated, deluxe) are not intents.
    private static readonly FrozenSet<string> AdaptWords = Set("graphic comic comics manga adaptation adapted retold");
    private static readonly FrozenSet<string> StudyWords = Set("sparknotes cliffsnotes cliffs spark study summary analysis");
    private static readonly FrozenSet<string> OmnibusWords = Set("omnibus trilogy collection boxed box");

    /// <summary>Strong enough alone; the rest need their pair.</summary>
    private static readonly FrozenSet<string> IntentSingles = Set(
        "graphic comic comics manga adaptation adapted retold sparknotes cliffsnotes omnibus trilogy");

    /// <summary>
    /// Two-word intents, including "spark notes" / "cliffs notes" (sparknotes/cliffsnotes typed as two words).
    /// </summary>
    private static readonly FrozenDictionary<(string, string), EditionIntent> IntentPairs = new Dictionary<(string, string), EditionIntent>
    {
        [("study", "guide")] = EditionIntent.StudyGuide,
        [("spark", "notes")] = EditionIntent.StudyGuide,
        [("cliffs", "notes")] = EditionIntent.StudyGuide,
        [("box", "set")] = EditionIntent.Omnibus,
        [("boxed", "set")] = EditionIntent.Omnibus,
        [("complete", "works")] = EditionIntent.Omnibus,
        [("graphic", "novel")] = EditionIntent.Adaptation,
    }.ToFrozenDictionary();

    private static readonly FrozenSet<string> IntentWords =
        Set(string.Join(' ', AdaptWords.Concat(StudyWords).Concat(OmnibusWords)) + " guide novel set works notes");

    private static readonly FrozenSet<string> Series = Set(
        "book books bk vol volume volumes part pt no number installment instalment sequel episode series sequence trilogy " +
        "saga chronicles cycle novel novels tome entry");

    private static readonly FrozenSet<string> OrdinalWords = Set(
        "first second third fourth fifth sixth seventh eighth ninth tenth eleventh twelfth 1st 2nd 3rd 4th 5th 6th 7th 8th 9th 10th");

    public static IReadOnlySet<string> Stop => StopWords;
    public static IReadOnlySet<string> Noise => NoiseWords;

    /// <summary>Intent words plus their pair words (guide, novel, set, works, notes).</summary>
    public static IReadOnlySet<string> AllIntentWords => IntentWords;
    public static IReadOnlySet<string> SeriesWords => Series;
    public static IReadOnlySet<string> Ordinals => OrdinalWords;

    /// <summary>
    /// Edition intents in folded tokens: adaptation and study-guide
    /// words only. Omnibus words are recognised but never reported (no measured evidence).
    /// </summary>
    public static IReadOnlySet<EditionIntent> Intents(IReadOnlyList<string> tokens)
    {
        var found = new HashSet<EditionIntent>();
        for (var i = 0; i + 1 < tokens.Count; i++)
            if (IntentPairs.TryGetValue((tokens[i], tokens[i + 1]), out var intent)) found.Add(intent);
        foreach (var t in tokens)
        {
            if (!IntentSingles.Contains(t)) continue;
            if (AdaptWords.Contains(t)) found.Add(EditionIntent.Adaptation);
            if (StudyWords.Contains(t)) found.Add(EditionIntent.StudyGuide);
            if (OmnibusWords.Contains(t)) found.Add(EditionIntent.Omnibus);
        }
        found.Remove(EditionIntent.Omnibus);
        return found;
    }

    /// <summary>
    /// The query asks for an installment ("second", "#3", "book 2", "bk5"). "first"/"1st" count only next to a series
    /// word ("first love" is not an installment request).
    /// </summary>
    public static bool HasOrdinal(CleanedQuery query)
    {
        if (HashNumber().IsMatch(query.Text)) return true;
        var toks = query.All;
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            var nearSeries = (i > 0 && Series.Contains(toks[i - 1])) || (i + 1 < toks.Count && Series.Contains(toks[i + 1]));
            if (OrdinalWords.Contains(t) && t is not ("first" or "1st")) return true;
            if (OrdinalWords.Contains(t) && nearSeries) return true;
            if (SeriesNumberToken().IsMatch(t)) return true;
            if (IsSmallNumber(t) && nearSeries) return true;
        }
        return false;
    }

    /// <summary>A number 1–20 written as digits or a word ("3", "three").</summary>
    internal static bool IsSmallNumber(string t) =>
        (t.Length > 0 && t.All(char.IsAsciiDigit) && t.TrimStart('0') is { Length: > 0 and <= 2 } v && int.Parse(v) is >= 1 and <= 20)
        || Numerals.WordValue(t) is >= 1 and <= 20;

    [GeneratedRegex(@"#\s*[0-9]{1,2}\b")]
    internal static partial Regex HashNumber();

    [GeneratedRegex(@"^(?:bk|vol|v|no|pt|book|part)[0-9]{1,2}$")]
    private static partial Regex SeriesNumberToken();

    private static FrozenSet<string> Set(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet();
}
