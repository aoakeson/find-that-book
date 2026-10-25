namespace FindThatBook.Infrastructure.Suggest;

/// <summary>Bounds of <c>GET /api/suggest</c>. The API validates; the index also clamps to stay safe.</summary>
public static class SuggestLimits
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 10;
    public const int MaxPrefixLength = 100;
}

/// <summary>
/// Autocomplete over the committed index of the most-edited Open Library works. Immutable once built,
/// so one instance serves every request. Three sorted key tables, searched by binary search on the folded prefix:
/// whole-title keys, word-aligned title keys and author keys. Three tiers, each by edition count then work id (works
/// are stored in that order, so "more editions" is just "smaller row index"):
/// <list type="number">
/// <item>exact whole title ("it" → It);</item>
/// <item>whole-title prefix ending on a word ("lord of the" → The Lord of the Rings);</item>
/// <item>everything else that matches: any title prefix, any title word, any author name or surname, full or
/// partial. Typing is usually mid-word, so these compete on editions alone: "hobb" → The Hobbit before Robin Hobb,
/// "potter" → Harry Potter before Potterism, "austen" → Pride and Prejudice, "tolk" → The Hobbit.</item>
/// </list>
/// A one-letter prefix is the start of typing a title: titles starting with it come first by editions, then the rest
/// ("s" → The Scarlet Letter, not the notebooks titled "S" nor "UK Continental Shelf…" through a stray word).
/// </summary>
public sealed class SuggestIndex : ISuggestIndex
{
    const string ResourceName = "FindThatBook.Infrastructure.Suggest.suggest-index.json.gz";

    // One parse per process (≈ 0.4 s warm, 0.8 s cold): API test hosts and the eval tool share it. The data is read-only.
    static readonly Lazy<SuggestIndex> embedded = new(LoadEmbedded);

    readonly SuggestRow[] works;
    readonly KeyTable titles;
    readonly KeyTable words;
    readonly KeyTable authors;

    /// <summary>The committed index, loaded on first use.</summary>
    public static SuggestIndex Embedded => embedded.Value;

    public string Version { get; }
    public int Count => works.Length;

    internal SuggestIndex(IEnumerable<SuggestRow> rows, string version)
    {
        Version = version;
        works = [.. rows.OrderByDescending(r => r.EditionCount).ThenBy(r => r.WorkId, StringComparer.Ordinal)];

        var titleKeys = new List<KeyEntry>(works.Length * 2);
        var wordKeys = new List<KeyEntry>(works.Length * 5);
        var authorKeys = new List<KeyEntry>(works.Length * 2);
        var authorCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        for (var i = 0; i < works.Length; i++)
        {
            foreach (var form in SuggestText.TitleForms(works[i].Title))
            {
                var text = string.Join(' ', form);
                titleKeys.Add(new KeyEntry(text, 0, i));
                foreach (var start in SuggestText.WordStarts(form)) wordKeys.Add(new KeyEntry(text, start, i));
            }
            if (works[i].Author is { } author)
            {
                if (!authorCache.TryGetValue(author, out var keys)) authorCache[author] = keys = SuggestText.AuthorKeys(author);
                foreach (var key in keys) authorKeys.Add(new KeyEntry(key, 0, i));
            }
        }
        titles = new KeyTable(titleKeys);
        words = new KeyTable(wordKeys);
        authors = new KeyTable(authorKeys);
    }

    public IReadOnlyList<Suggestion> Lookup(string prefix, int limit)
    {
        if (string.IsNullOrWhiteSpace(prefix) || limit <= 0) return [];
        if (prefix.Length > SuggestLimits.MaxPrefixLength) prefix = prefix[..SuggestLimits.MaxPrefixLength];
        limit = Math.Min(limit, SuggestLimits.MaxLimit);
        var key = SuggestText.PrefixKey(prefix);
        if (key.Length == 0) return [];

        var picks = new Picks(limit);
        int t = titles.LowerBound(key), w = words.LowerBound(key), a = authors.LowerBound(key);
        if (key.Length > 1)
        {
            titles.Offer(t, key, Fit.Exact, SuggestMatch.Title, picks);
            picks.EndTier();
            titles.Offer(t, key, Fit.WordEnd, SuggestMatch.Title, picks);
            picks.EndTier();
            titles.Offer(t, key, Fit.Any, SuggestMatch.Title, picks);
        }
        else
        {
            titles.Offer(t, key, Fit.Any, SuggestMatch.Title, picks);
            picks.EndTier();
        }
        words.Offer(w, key, Fit.Any, SuggestMatch.Word, picks);
        authors.Offer(a, key, Fit.Any, SuggestMatch.Author, picks);
        picks.EndTier();

        var result = new Suggestion[picks.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var row = works[picks.Work(i)];
            result[i] = new Suggestion(row.WorkId, row.Title, row.Author, row.FirstPublishYear, row.CoverId, row.EditionCount, picks.Match(i));
        }
        return result;
    }

    internal static SuggestIndex FromFile(SuggestIndexFile file) => new(file.Works, file.Version);

    static SuggestIndex LoadEmbedded()
    {
        using var stream = typeof(SuggestIndex).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded suggest index '{ResourceName}' is missing from the build.");
        return FromFile(SuggestIndexFile.Read(stream));
    }

    /// <summary>A key: <c>Text[Start..]</c>, so every word-aligned suffix of a title shares the title's string.</summary>
    readonly record struct KeyEntry(string Text, int Start, int Work)
    {
        public ReadOnlySpan<char> Key => Text.AsSpan(Start);
    }

    /// <summary>Which keys starting with the prefix count: equal to it, ending there or at a word break, or any.</summary>
    enum Fit { Exact, WordEnd, Any }

    sealed class KeyTable
    {
        readonly KeyEntry[] entries;

        public KeyTable(List<KeyEntry> list)
        {
            entries = [.. list];
            Array.Sort(entries, static (a, b) =>
            {
                var c = a.Key.SequenceCompareTo(b.Key);
                return c != 0 ? c : a.Work.CompareTo(b.Work);
            });
        }

        /// <summary>First key ≥ the prefix; the keys starting with it follow contiguously (ordinal order).</summary>
        public int LowerBound(string prefix)
        {
            int lo = 0, hi = entries.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >>> 1;
                if (entries[mid].Key.SequenceCompareTo(prefix) < 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Offers every work whose key starts with <paramref name="prefix"/> and continues as <paramref name="fit"/>.</summary>
        public void Offer(int from, string prefix, Fit fit, SuggestMatch match, Picks picks)
        {
            for (var i = from; i < entries.Length && !picks.Full; i++)
            {
                var key = entries[i].Key;
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) return;
                var fits = fit switch
                {
                    Fit.Exact => key.Length == prefix.Length,
                    Fit.WordEnd => key.Length == prefix.Length || key[prefix.Length] == ' ',
                    _ => true,
                };
                if (fits) picks.Offer(entries[i].Work, match);
            }
        }
    }

    /// <summary>
    /// The result in rank order. Works offered during a tier are kept sorted by row index (best first), at most the
    /// room left; <see cref="EndTier"/> appends them after the earlier tiers. A work is taken once, at its first tier;
    /// in the last tier titles, then title words, then authors are offered, so a work keeps the first of those labels.
    /// At most 10 entries, so linear membership checks are fine.
    /// </summary>
    sealed class Picks(int limit)
    {
        readonly int[] works = new int[limit];
        readonly SuggestMatch[] matches = new SuggestMatch[limit];
        int pending;

        public int Count { get; private set; }
        /// <summary>Earlier tiers filled the result. (A full tier buffer is not "full": a later key may hold a better work.)</summary>
        public bool Full => Count == works.Length;
        public int Work(int i) => works[i];
        public SuggestMatch Match(int i) => matches[i];

        public void Offer(int work, SuggestMatch match)
        {
            var room = works.Length - Count;
            if (room == 0) return;
            if (pending == room && work >= works[Count + pending - 1]) return;
            if (works.AsSpan(0, Count + pending).Contains(work)) return;
            var pos = pending < room ? pending++ : room - 1;
            while (pos > 0 && works[Count + pos - 1] > work)
            {
                works[Count + pos] = works[Count + pos - 1];
                matches[Count + pos] = matches[Count + pos - 1];
                pos--;
            }
            works[Count + pos] = work;
            matches[Count + pos] = match;
        }

        public void EndTier()
        {
            Count += pending;
            pending = 0;
        }
    }
}
