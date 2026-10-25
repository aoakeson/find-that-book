using System.Globalization;
using System.Text.RegularExpressions;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>Facts about the whole pool and query that every work's classification reads.</summary>
    private sealed class RankingContext
    {
        public RankingContext(CleanedQuery query, QueryHypothesis? rawHypothesis, NormalizedHypothesis? hypothesis, RankerOptions options,
            List<Candidate> works)
        {
            Topic = options.Topics ? TopicRequest.From(query, rawHypothesis) : null;
            Query = query;
            Hypothesis = hypothesis;
            Options = options;
            Works = works;
            Search = query.Search;
            SoftTokens = options.SoftTokens ? query.SoftTokens : new HashSet<string>();
            HypothesisCharacterTokens = hypothesis?.CharacterTokens() ?? [];
            // Typed stop/noise words, and request words ("find me", "recommend"), count when a title contains them.
            OptionalTitleWords = query.All.Distinct()
                .Where(t => (query.Optional.Contains(t) || (options.Topics && query.RequestTokens.Contains(t))) && !Vocabulary.Stop.Contains(t))
                .ToList();
            PoolSurnames = works.SelectMany(w => w.Authors).Select(a => PersonNames.Surname(a.Display)).ToHashSet(StringComparer.Ordinal);
            var totalEditions = Math.Max(1L, works.Sum(w => (long)w.Editions));
            DominantAuthorTokens = FindDominantAuthorTokens(works, totalEditions);
            AuthorPhrase = FindAuthorPhrase(works, totalEditions);

            foreach (var work in works)
            {
                if (!SurnamesBySameTitleRecord.TryGetValue(work.TitleKey, out var byId))
                    SurnamesBySameTitleRecord[work.TitleKey] = byId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var surnames = work.Authors.Select(a => PersonNames.Surname(a.Display)).ToHashSet(StringComparer.Ordinal);
                foreach (var credited in work.Roles.Keys) surnames.Add(credited.Split(' ')[^1]);
                byId[work.Id] = surnames;
            }

            HypothesisVariants = BuildHypothesisVariants(query, hypothesis, options);
            TypedVolumeNumber = TypedVolume(query);
        }

        public CleanedQuery Query { get; }

        /// <summary>Topic request (interpreter intent, else the cleaner's "a book about …" frame); null for a book search.</summary>
        public TopicRequest? Topic { get; }

        /// <summary>Set once ranking decides the topic order applies (a topic request whose typed text is not a title).</summary>
        public bool TopicMode { get; set; }
        public NormalizedHypothesis? Hypothesis { get; }
        public RankerOptions Options { get; }
        public List<Candidate> Works { get; }
        public IReadOnlyList<string> Search { get; }

        /// <summary>Year-shaped words kept in the search only because few other words were typed (IN-044).</summary>
        public IReadOnlySet<string> SoftTokens { get; }

        public HashSet<string> HypothesisCharacterTokens { get; }

        /// <summary>Typed stop/noise words (in typed order) that count as matched when a title contains them ("the collector").</summary>
        public List<string> OptionalTitleWords { get; }

        public HashSet<string> PoolSurnames { get; }

        /// <summary>Typed words naming an author who holds ≥ 30% of the pool's editions ("dickens").</summary>
        public HashSet<string> DominantAuthorTokens { get; }

        /// <summary>Name key of the author when the whole query (≥ 2 words) is that author's name ("henry james").</summary>
        public string? AuthorPhrase { get; }

        /// <summary>Title key → work id → surnames credited on that record (authors and parsed contributors).</summary>
        public Dictionary<string, Dictionary<string, HashSet<string>>> SurnamesBySameTitleRecord { get; } = new(StringComparer.Ordinal);

        public List<(IReadOnlyList<string> Tokens, TitleSource Source)> HypothesisVariants { get; }

        /// <summary>A volume number the query asks for ("volume 1"), as digits.</summary>
        public string? TypedVolumeNumber { get; }

        /// <summary>Whether the extracted year or any soft year token matches this work.</summary>
        public bool YearMatches(Candidate work)
        {
            if (work.Year is not { } published) return false;
            if (Query.Year is { } year) return Math.Abs(published - year) <= 1;
            if (!Options.SoftTokens) return false;
            foreach (var t in Search)
                if (SoftTokens.Contains(t) && !work.Full.Contains(t) && t.Length == 4 && t.All(char.IsAsciiDigit)
                    && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var soft) && soft is >= 1000 and <= 2099
                    && Math.Abs(published - soft) <= 1)
                    return true;
            return false;
        }

        private HashSet<string> FindDominantAuthorTokens(List<Candidate> works, long totalEditions)
        {
            var editionsByNameToken = new Dictionary<string, long>(StringComparer.Ordinal);
            var tokenOrder = new List<string>();
            foreach (var work in works)
                foreach (var author in work.Authors)
                {
                    if (!work.Primary.Contains(author.NameKey)) continue;
                    foreach (var t in PersonNames.Tokens(author.Display))
                    {
                        if (t.Len() < 3) continue;
                        if (!editionsByNameToken.ContainsKey(t)) { editionsByNameToken[t] = 0; tokenOrder.Add(t); }
                        editionsByNameToken[t] += work.Editions;
                    }
                }
            return Search.Where(t => tokenOrder.Any(n => TokenMatch.Equal(t, n) && editionsByNameToken[n] >= Weights.DominantAuthorShare * totalEditions))
                .ToHashSet(StringComparer.Ordinal);
        }

        private string? FindAuthorPhrase(List<Candidate> works, long totalEditions)
        {
            if (Search.Count < 2) return null;
            var editionsByAuthor = new Dictionary<string, long>(StringComparer.Ordinal);
            var authorOrder = new List<string>();
            foreach (var work in works)
                foreach (var author in work.Authors)
                {
                    var nameTokens = PersonNames.Tokens(author.Display).Where(t => t.Len() > 1).ToList();
                    if (!Search.All(s => nameTokens.Any(t => TokenMatch.Equal(s, t)))) continue;
                    if (!editionsByAuthor.ContainsKey(author.NameKey)) { editionsByAuthor[author.NameKey] = 0; authorOrder.Add(author.NameKey); }
                    editionsByAuthor[author.NameKey] += work.Editions;
                }
            string? best = null;
            foreach (var key in authorOrder) // ties go to the ordinal-lower key, so pool order never matters
                if (best is null || editionsByAuthor[key] > editionsByAuthor[best]
                    || (editionsByAuthor[key] == editionsByAuthor[best] && string.CompareOrdinal(key, best) < 0))
                    best = key;
            return best is not null && editionsByAuthor[best] >= Weights.AuthorPhraseShare * totalEditions ? best : null;
        }
    }

    /// <summary>
    /// The interpreter's title variants: its main and full title, then its original-language and English titles. R3-0390 /
    /// R3-0283: a hypothesis subtitle the user typed is required in the interpreter's own variants (the bare main title no
    /// longer counts), and a typed volume number the hypothesis title carries is required in every variant (an
    /// original-language title without the volume no longer counts).
    /// </summary>
    private static List<(IReadOnlyList<string>, TitleSource)> BuildHypothesisVariants(CleanedQuery query, NormalizedHypothesis? hypothesis, RankerOptions options)
    {
        var variants = new List<(IReadOnlyList<string>, TitleSource)>();
        if (hypothesis is null) return variants;
        var volume = options.RequiredTitleWords && hypothesis.HasTitle && TypedVolume(query) is { } typed && hypothesis.Full.Contains(typed) ? typed : null;
        var subtitle = options.RequiredTitleWords ? TypedSubtitle(query, hypothesis) : [];
        IReadOnlyList<string> With(IReadOnlyList<string> tokens, IEnumerable<string> required)
        {
            var missing = required.Where(r => !tokens.Contains(r)).Distinct().ToList();
            return missing.Count == 0 ? tokens : [.. tokens, .. missing];
        }
        IEnumerable<string> VolumeOnly() => volume is null ? [] : [volume];
        if (hypothesis.HasTitle)
        {
            variants.Add((With(hypothesis.Main, subtitle.Concat(VolumeOnly())), TitleSource.Hypothesis));
            variants.Add((With(hypothesis.Full, VolumeOnly()), TitleSource.Hypothesis));
        }
        foreach (var alternate in hypothesis.Alternates)
        {
            var source = alternate.IsOriginal ? TitleSource.OriginalTitle : TitleSource.Hypothesis;
            variants.Add((With(alternate.Main, VolumeOnly()), source));
            variants.Add((With(alternate.Full, VolumeOnly()), source));
        }
        return variants;
    }

    /// <summary>The hypothesis subtitle's words when the user typed ≥ 2 of them (words not in its main title), else none.</summary>
    private static IReadOnlyList<string> TypedSubtitle(CleanedQuery query, NormalizedHypothesis hypothesis)
    {
        if (!hypothesis.HasTitle) return [];
        var subtitle = hypothesis.Full.Skip(hypothesis.Main.Count).Where(w => !hypothesis.Main.Contains(w)).Distinct().ToList();
        var typed = Numerals.ToDigits(query.Search);
        return subtitle.Count(w => typed.Any(t => TokenMatch.Supports(t, w))) >= 2 ? subtitle : [];
    }

    /// <summary>A volume number typed next to a series word ("volume 1", "vol 2", "bk3", "#4"), as digits.</summary>
    internal static string? TypedVolume(CleanedQuery query)
    {
        var all = Numerals.ToDigits(query.All);
        for (var i = 0; i < all.Count; i++)
        {
            var joined = VolumeToken().Match(all[i]);
            if (joined.Success) return VolumeNumber(joined.Groups[1].Value);
            if (i + 1 < all.Count && VolumeWords.Contains(all[i]) && VolumeNumber(all[i + 1]) is { } next) return next;
        }
        var hash = HashVolume().Match(TextFolding.Fold(query.Text));
        return hash.Success ? VolumeNumber(hash.Groups[1].Value) : null;
    }

    /// <summary>An ASCII number 1–99 as canonical digits; anything else (Unicode digits "٢", junk) is no volume.</summary>
    private static string? VolumeNumber(string s)
        => s.Length is >= 1 and <= 2 && s.All(char.IsAsciiDigit)
           && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 99
            ? n.ToString(CultureInfo.InvariantCulture) : null;

    private static readonly HashSet<string> VolumeWords = new(StringComparer.Ordinal) { "vol", "volume", "book", "bk", "part", "pt", "no", "number", "tome" };

    [GeneratedRegex(@"^(?:vol|bk|pt|no|book|part|v)([0-9]{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeToken();

    [GeneratedRegex(@"#\s*([0-9]{1,2})(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex HashVolume();
}
