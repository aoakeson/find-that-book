using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

/// <summary>The title facts shown for a work (MatchEvidence): its own, or the group leader's when it took the leader's tier.</summary>
internal sealed record TitleEvidence(bool Exact, double Score, TitleSource Source, string? EditionTitle, IReadOnlyList<string> Tokens)
{
    public static TitleEvidence None { get; } = new(false, 0, TitleSource.None, null, []);
}

/// <summary>An author entry of one work: name key (identity), display name and Open Library key.</summary>
internal sealed record WorkAuthor(string NameKey, string Display, string? OlKey);

/// <summary>
/// One pool work while it is being ranked. Built fresh for every <see cref="MatchRanker.Rank"/>
/// call, so ranking holds no state between calls.
/// </summary>
internal sealed class Candidate
{
    public Candidate(CandidateWork work, int order)
    {
        Work = work;
        Order = order;
        // Duplicate author records merge by name key (Mark Twain ×2); author order carries no meaning (01 Q2).
        var authors = new List<WorkAuthor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < work.AuthorNames.Count; i++)
        {
            var key = PersonNames.Key(work.AuthorNames[i]);
            if (key.Length == 0 || !seen.Add(key)) continue;
            var olKey = i < work.AuthorKeys.Count && !string.IsNullOrWhiteSpace(work.AuthorKeys[i]) ? work.AuthorKeys[i] : null;
            authors.Add(new WorkAuthor(key, work.AuthorNames[i], olKey));
        }
        Authors = authors;
        Year = work.FirstPublishYear is > 0 ? work.FirstPublishYear : null;
        TitleKey = TitleText.Key(work.Title);
        var sets = TitleText.Sets(work.Title, work.Subtitle);
        Main = sets.Main;
        Full = sets.Full;
        FoldedTitle = TextFolding.Fold(work.Title);
        FoldedSubtitle = TextFolding.Fold(work.Subtitle ?? "");
        EditionTitles = work.EditionTitles.Where(t => !string.IsNullOrEmpty(t)).Take(3).ToList();
        EditionSets = EditionTitles.Select(t => TitleText.Sets(t)).ToList();
        EditionKeys = EditionTitles.Select(TitleText.Key).Where(k => k != TitleKey).ToHashSet(StringComparer.Ordinal);
        AlternativeNames = work.AuthorAlternativeNames.Take(60).ToList();
    }

    public CandidateWork Work { get; }
    public string Id => Work.WorkId;
    public int Order { get; }
    public IReadOnlyList<WorkAuthor> Authors { get; }
    public int Editions => Work.EditionCount;
    public int ReadingLog => Work.ReadingLogCount;
    public bool HasCover => Work.CoverId is > 0;
    public int? Year { get; }
    public string TitleKey { get; }
    public IReadOnlyList<string> Main { get; }
    public IReadOnlyList<string> Full { get; }
    public string FoldedTitle { get; }
    public string FoldedSubtitle { get; }
    public IReadOnlyList<string> EditionTitles { get; }
    public IReadOnlyList<TitleTokens> EditionSets { get; }
    public HashSet<string> EditionKeys { get; }
    public IReadOnlyList<string> AlternativeNames { get; }
    public IReadOnlyList<string> Persons => Work.Persons;

    // ---- resolve (canonical-in-pool, roles, propagation) ----
    public IReadOnlyDictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
    public HashSet<string> Primary { get; set; } = [];
    public HashSet<string> Contributors { get; set; } = [];
    /// <summary>Set when propagation left no primary: the group canonical whose primaries this work inherits.</summary>
    public Candidate? InheritedFrom { get; set; }
    /// <summary>True when no larger same-title peer shares an author (the work is its own canonical; DA-026).</summary>
    public bool SelfCanonical { get; set; }

    // ---- classify ----
    public HashSet<string> AuthorTokens { get; set; } = [];
    public HashSet<string> AuthorKeys { get; set; } = [];
    public HashSet<string> AliasTokens { get; set; } = [];
    /// <summary>The author evidence exists only through an alternative name (pen / real name).</summary>
    public bool ViaAlternativeName { get; set; }
    public bool BySurname { get; set; }
    public bool AuthorFromHypothesis { get; set; }
    public double CharacterCoverage { get; set; }
    public bool Exact { get; set; }
    public bool ExactTyped { get; set; }
    public double TitleScore { get; set; }
    /// <summary>Share of the typed (or interpreted) title words this work's best title holds.</summary>
    public double TitlePrecision { get; set; }
    public TitleSource TitleSource { get; set; }
    public string? MatchedEditionTitle { get; set; }
    public TitleEvidence ShownTitle { get; set; } = TitleEvidence.None;
    /// <summary>Full-title tokens when the title match needed the subtitle, else "" (keeps such a work out of the main title's group).</summary>
    public string SubtitleKey { get; set; } = "";
    public AuthorRole? Role { get; set; }
    public MatchTier Tier { get; set; }
    public bool Foreign { get; set; }
    public bool AuthorOnly { get; set; }
    public bool IntentHit { get; set; }
    public bool YearMatched { get; set; }
    public List<string> Penalties { get; set; } = [];
    /// <summary>The title names one part of a work ("(下)", "vol. 2"; IN-035).</summary>
    public bool VolumePart { get; set; }
    public IReadOnlyList<string> MatchedContributors { get; set; } = [];
    /// <summary>Same-title records crediting a typed surname this work leaves unexplained, and those surnames.</summary>
    public HashSet<string> NamedRivals { get; set; } = [];
    public HashSet<string> NamedTokens { get; set; } = [];
    /// <summary>Ranked below a record that explains a typed contributor; it cannot make the status ambiguous by outscoring it.</summary>
    public bool ExcludedByContributor { get; set; }
    /// <summary>Typed search words that do not name one of this work's authors.</summary>
    public IReadOnlyList<string> NonAuthorTokens { get; set; } = [];
    public double Score { get; set; }

    // ---- topic requests ----
    /// <summary>Subject headings that contain a topic term (all its words, plural-tolerant).</summary>
    public List<string> MatchedSubjects { get; set; } = [];
    /// <summary>Share of the topic terms some subject heading of this work contains.</summary>
    public double SubjectCoverage { get; set; }
    /// <summary>The interpreter suggested this book for the topic, and this record is it (title and author agree).</summary>
    public bool SuggestedByInterpreter { get; set; }
    /// <summary>
    /// First ordering key: 0 for everyone, except for a topic request: in topic mode works without topic evidence get 1;
    /// when the typed text is a title instead, works without that exact title get 1.
    /// </summary>
    public int OrderGroup { get; set; }

    // ---- dedupe ----
    public List<Candidate> Dups { get; set; } = [];

    public WorkAuthor? Author(string nameKey)
    {
        foreach (var a in Authors) if (a.NameKey == nameKey) return a;
        return null;
    }

    public bool SharesAuthorWith(Candidate other)
    {
        foreach (var a in Authors) if (other.Author(a.NameKey) is not null) return true;
        return false;
    }

    /// <summary>Primary name keys, or the inherited ones when the work has none of its own.</summary>
    public IEnumerable<string> PrimaryOrInherited => Primary.Count > 0 ? Primary : InheritedFrom?.Primary ?? [];

    /// <summary>
    /// (edition count, reading-log count, has a cover): which record is the canonical / representative. A full tie goes
    /// to the lower work id (ordinal), so the choice never depends on pool order.
    /// </summary>
    public static bool Outranks(Candidate a, Candidate b)
        => a.Editions != b.Editions ? a.Editions > b.Editions
            : a.ReadingLog != b.ReadingLog ? a.ReadingLog > b.ReadingLog
            : a.HasCover != b.HasCover ? a.HasCover
            : string.CompareOrdinal(a.Id, b.Id) < 0;

    /// <summary>The most-edited record (see <see cref="Outranks"/>).</summary>
    public static Candidate MostEdited(IEnumerable<Candidate> xs)
    {
        Candidate? best = null;
        foreach (var x in xs) if (best is null || Outranks(x, best)) best = x;
        return best ?? throw new InvalidOperationException("empty");
    }
}
