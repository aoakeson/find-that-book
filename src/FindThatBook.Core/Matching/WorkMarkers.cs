using System.Text.RegularExpressions;

namespace FindThatBook.Core.Matching;

/// <summary>
/// "Different work" classes (omnibus, study guide, adaptation, volume part) read from the folded title. Each is a score
/// penalty or an edition-intent signal, never a filter.
/// </summary>
internal static partial class WorkMarkers
{
    public const string Omnibus = "omnibus";
    public const string Study = "study";
    public const string Adapt = "adapt";
    public const string Volume = "volume";

    private static readonly IReadOnlyDictionary<string, string[]> QueryWords = new Dictionary<string, string[]>
    {
        [Omnibus] = ["collection", "set", "box", "boxed", "omnibus", "trilogy", "series", "complete", "works", "novels"],
        [Study] = ["sparknotes", "cliffsnotes", "cliffs", "notes", "study", "guide", "summary", "analysis", "companion"],
        [Adapt] = ["graphic", "adaptation", "adapted", "retold", "manga", "comic", "movie", "play"],
    };


    /// <summary>Classes this work belongs to regardless of the query, for edition-intent hits.</summary>
    public static HashSet<string> Kinds(string foldedTitle, string foldedSubtitle, IEnumerable<string> foldedAuthorNames, IEnumerable<string> roles,
        bool bareStudyWords = false)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        if (OmnibusPattern().IsMatch(foldedTitle)) kinds.Add(Omnibus);
        var blob = foldedTitle + " " + string.Join(' ', foldedAuthorNames) + " " + foldedSubtitle;
        if (StudyPattern().IsMatch(blob) || (bareStudyWords && BareStudyPattern().IsMatch(blob))) kinds.Add(Study);
        if (AdaptPattern().IsMatch(foldedTitle + " " + foldedSubtitle) || roles.Any(r => r.Contains(Adapt, StringComparison.Ordinal)))
            kinds.Add(Adapt);
        return kinds;
    }

    /// <summary>Penalised classes: the class matches the title and the query has no word of that class.</summary>
    public static List<string> Penalties(string foldedTitle, IEnumerable<string> foldedAuthorNames, IReadOnlyList<string> queryTokens,
        bool bareStudyWords = false)
    {
        var penalties = new List<string>();
        if (OmnibusPattern().IsMatch(foldedTitle) && !Mentions(queryTokens, Omnibus)) penalties.Add(Omnibus);
        var blob = foldedTitle + " " + string.Join(' ', foldedAuthorNames);
        if ((StudyPattern().IsMatch(blob) || (bareStudyWords && BareStudyPattern().IsMatch(blob))) && !Mentions(queryTokens, Study)) penalties.Add(Study);
        if (AdaptPattern().IsMatch(foldedTitle) && !Mentions(queryTokens, Adapt)) penalties.Add(Adapt);
        return penalties;
    }

    /// <summary>
    /// IN-035: an explicit part marker ("ノルウェイの森 (下)", "〈上〉", "vol. 2", "part 1") names one part of a work. Title keys
    /// drop (…), so without this the part ties (and folds into) the whole work. Series positions ("(Twilight, #1)",
    /// "Book 1") are not parts. A penalty only when the query asks for no volume.
    /// </summary>
    public static bool HasVolumeMarker(string foldedTitle) => VolumePattern().IsMatch(foldedTitle);

    public static bool IsOmnibus(string foldedTitle) => OmnibusPattern().IsMatch(foldedTitle);

    private static bool Mentions(IReadOnlyList<string> queryTokens, string kind) => queryTokens.Any(t => QueryWords[kind].Contains(t));

    [GeneratedRegex(@"[(（〈《\[]\s*(上|中|下|上巻|中巻|下巻|前編|後編)\s*[)）〉》\]]|[上中下]巻|\b(vol|volume|tome|part|pt)\.?\s*\d{1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex VolumePattern();

    [GeneratedRegex(@"\[collection/set\]|\bcollection\b|\bbox(ed)? set\b|\bomnibus\b|\bcomplete (works|novels)\b|\btrilogy\b|\(series\)|\bseries\b|\s/\s|^(the )?(works|novels|selected works)\b|\b\d+\s*-\s*\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex OmnibusPattern();

    [GeneratedRegex(@"sparknotes|spark notes|cliffs?notes|cliffs notes|study guide|york notes|literature guide|reader.?s guide|teaching guide|lesson plan|\bsummary\b|\banalysis\b|\bcompanion\b|\bnotes on\b|guide to\b|monarch notes|bookrags|gradesaver|critical (essays|interpretations)|casebook|\bworkbook\b", RegexOptions.CultureInvariant)]
    private static partial Regex StudyPattern();

    [GeneratedRegex(@"\bstudy\b|\bguide\b", RegexOptions.CultureInvariant)]
    private static partial Regex BareStudyPattern();

    [GeneratedRegex(@"\[adaptation\]|\(adaptation\)|graphic novel|\bretold\b|\badapted\b|\bmanga\b|\bcomic\b|movie book|screenplay|young readers|coloring|activity book|\bpicture book\b|\bplay\b", RegexOptions.CultureInvariant)]
    private static partial Regex AdaptPattern();
}
