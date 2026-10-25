namespace FindThatBook.Core.Normalization;

/// <summary>Token and title similarity.</summary>
public static class TokenMatch
{
    /// <summary>Optimal string alignment (Damerau) distance; returns cap + 1 when the lengths differ by more than cap.</summary>
    public static int Distance(string a, string b, int cap = 3)
    {
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// Typo-tolerant equality used for ranking. Numbers are never fuzzy, tokens under 4 letters must be equal, and a
    /// prefix pair is never fuzzy-equal (prince/princess, king/kingdom; IN-037) unless it is a plural or doubled last
    /// letter. Otherwise one edit, or two on ≥ 8-letter tokens with the same first letter.
    /// </summary>
    public static bool Equal(string a, string b)
    {
        if (a == b) return true;
        if (TextFolding.IsDigits(a) || TextFolding.IsDigits(b)) return false;
        if (Math.Min(a.Length, b.Length) < 4) return false;
        if ((a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)) && !PrefixVariant(a, b)) return false;
        var d = Distance(a, b);
        return d <= 1 || (d == 2 && Math.Max(a.Length, b.Length) >= 8 && a[0] == b[0]);
    }

    /// <summary>Plural/doubled-last-letter pairs (ghost/ghosts, giver/giverr) count as equal despite the prefix guard.</summary>
    public static bool PrefixVariant(string a, string b)
    {
        var (s, l) = a.Length < b.Length ? (a, b) : (b, a);
        return s.Length > 0 && l.StartsWith(s, StringComparison.Ordinal) && (l == s + "s" || l == s + s[^1]);
    }

    /// <summary>
    /// Looser equality used only to decide whether a typed word supports the LLM title, never for ranking:
    /// <see cref="Equal"/>, or one edit on ≥ 3 letters (tee/tea), or two edits on ≥ 5 letters with the same first letter
    /// (jumbys/jumbies). Prefix pairs never count.
    /// </summary>
    public static bool Supports(string typed, string titleWord)
    {
        if (Equal(typed, titleWord) || PrefixVariant(typed, titleWord)) return true;
        if (TextFolding.IsDigits(typed) || TextFolding.IsDigits(titleWord)) return false;
        if (typed.StartsWith(titleWord, StringComparison.Ordinal) || titleWord.StartsWith(typed, StringComparison.Ordinal)) return false;
        var shorter = Math.Min(typed.Length, titleWord.Length);
        var d = Distance(typed, titleWord);
        return (shorter >= 3 && d <= 1) || (shorter >= 5 && d <= 2 && typed[0] == titleWord[0]);
    }

    /// <summary>
    /// Title similarity: literal partners are paired first, fuzzy
    /// <see cref="Equal"/> fills the rest; exact needs every token of both sides paired, all literally. Query tokens
    /// get number words as digits. Repeated tokens on either side count once (IN-011: "hobbit hobbit hobbit").
    /// </summary>
    public static TitleMatch Score(IReadOnlyList<string> query, IReadOnlyList<string> title)
    {
        if (query.Count == 0 || title.Count == 0) return TitleMatch.NoMatch;
        var q = Numerals.ToDigits(query).Distinct().ToList();
        var t = title.Distinct().ToList();
        var (matched, literal) = Overlap(q, t);
        var p = (double)matched / q.Count;
        var r = (double)matched / t.Count;
        var exact = matched == q.Count && matched == t.Count && literal;
        return new TitleMatch(exact, exact ? 1.0 : (p + r) / 2, p, r);
    }

    /// <summary>
    /// Every query token pairs with a title token (same count), literal partners first, and each non-literal pair is
    /// one edit apart on words of ≥ 4 letters ("murdr"/"murder", "dogfsh"/"dogfish"): a typed title complete within
    /// typos counts as exact.
    /// </summary>
    public static bool FuzzyComplete(IReadOnlyList<string> query, IReadOnlyList<string> title)
    {
        if (query.Count == 0 || title.Count == 0) return false;
        var left = title.Distinct().ToList();
        var rest = new List<string>();
        foreach (var x in Numerals.ToDigits(query).Distinct())
        {
            if (!left.Remove(x)) rest.Add(x);
        }
        if (rest.Count != left.Count) return false;
        foreach (var x in rest)
        {
            var i = left.FindIndex(y => (Equal(x, y) || PrefixVariant(x, y)) && Math.Min(x.Length, y.Length) >= 4 && Distance(x, y) <= 1);
            if (i < 0) return false;
            left.RemoveAt(i);
        }
        return left.Count == 0;
    }

    /// <summary>(matched count, every match literal): literal pass first, then greedy fuzzy, each title token used once.</summary>
    public static (int Matched, bool AllLiteral) Overlap(IReadOnlyList<string> query, IReadOnlyList<string> title)
    {
        var left = title.ToList();
        var matched = 0;
        var literal = true;
        var rest = new List<string>();
        foreach (var x in query)
        {
            if (left.Remove(x)) matched++;
            else rest.Add(x);
        }
        foreach (var x in rest)
        {
            var i = left.FindIndex(y => Equal(x, y));
            if (i < 0) continue;
            matched++;
            literal = literal && x == left[i];
            left.RemoveAt(i);
        }
        return (matched, literal);
    }
}

/// <param name="Precision">Share of query tokens matched (p).</param>
/// <param name="Recall">Share of title tokens matched (r).</param>
public sealed record TitleMatch(bool Exact, double Score, double Precision, double Recall)
{
    public static TitleMatch NoMatch { get; } = new(false, 0, 0, 0);
}
