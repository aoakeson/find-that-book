using System.Globalization;
using System.Text.RegularExpressions;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Normalization;

/// <summary>
/// Query cleaning: markup cleanup → tokens → stop/noise/year classification → search tokens in typed order, digit
/// form. Rules for specific inputs: soft years (IN-044), repeated words (IN-011), junk tokens (IN-012), split elisions
/// (IN-029), name suffixes (IN-065) and series numbers after a removed "book" (IN-057).
/// </summary>
public static partial class QueryCleaner
{
    /// <summary>An ASCII token longer than this is not a word ("tolkien hobbit xxxx…", IN-012); it is never sent.</summary>
    public const int MaxTokenLength = 40;

    private static readonly IReadOnlySet<string> NameSuffixes = new HashSet<string> { "jr", "sr" };

    /// <summary>
    /// Cleans a validated query. Field contract for the ranker:
    /// <list type="bullet">
    /// <item><c>Search</c>: content tokens in typed order, each once, number words as digits (a lone
    /// "one"/"zero" stays a word). This is exactly what call 1 sends.</item>
    /// <item><c>Optional</c>: every token not kept as content (stop, noise, extracted year, the dropped tokens below),
    /// in word form. Never sent; counts as matched when a title contains it, so "jungle book" still matches The Jungle Book.</item>
    /// <item><c>SoftTokens</c> (IN-044): year-shaped tokens that stayed in <c>Search</c> only because fewer than two other
    /// content words remain ("the hobbit 1937" → {1937}, "1984 orwell" → {1984}). They are sent, but for title
    /// exactness they are optional unless the candidate title contains them, so <i>The Hobbit</i> is exact for "the
    /// hobbit 1937" and <i>Nineteen Eighty-Four</i> (main tokens [1984]) still needs it. Empty when the year is the
    /// whole query ("1984", "1776": the number is the title).</item>
    /// <item><c>Year</c>: set only when the year was extracted (≥ 2 other content words); then it is in <c>Optional</c>.</item>
    /// <item><c>Head</c>: content tokens before a bare "or" that is not the first token; <c>All</c>: every token, with
    /// repeats, for intents and <see cref="Vocabulary.HasOrdinal"/>.</item>
    /// <item><c>RequestTokens</c>: words of a leading request phrase ("can you find", "i'm looking for a book called")
    /// and of a topic frame ("a funny book about") taken out of <c>Search</c>; they are in <c>Optional</c> too. Only
    /// when a content word remains, and ambiguous phrases only when a request follows (Find Me, Show Me a Sign).</item>
    /// <item><c>Topic</c>: up to 3 subject phrases of up to 4 words after a book-word + preposition frame ("books about
    /// the french revolution" → ["french revolution"], "bears and wolves" → ["bears", "wolves"]), within one sentence;
    /// <c>TopicPreferences</c>: audience/form words typed with it as subject words to prefer ("for kids" → juvenile,
    /// "a novel" → fiction); <c>TopicAuthor</c>: the words after "by". All empty without a frame noun: "about a boy",
    /// "on the road" and "we need to talk about kevin" stay title searches.</item>
    /// </list>
    /// </summary>
    public static CleanedQuery Clean(string raw)
    {
        var sanitized = QueryValidator.Sanitize(raw);
        var text = QueryValidator.Sanitize(QueryMarkup.Clean(sanitized)); // decoding can yield &#8203; or %00 again
        var all = TextFolding.Tokens(text);
        var dropped = DroppedPositions(all, text);
        var request = RequestPhrases.Find(text, all);
        // Request and topic-frame words leave the search text only when a content word remains ("find me" alone is a title).
        var removed = all.Where((t, i) => IsContent(t) && !dropped.Contains(i) && !request.Positions.Contains(i)).Any()
            ? request.Positions
            : new HashSet<int>();
        dropped.UnionWith(removed);

        var years = all.Where(IsYear).ToList();
        var content = all.Where((t, i) => IsContent(t) && !IsYear(t) && !dropped.Contains(i)).ToList();
        int? year = null;
        IReadOnlySet<string> soft = new HashSet<string>();
        if (years.Count > 0)
        {
            year = int.Parse(years[^1], CultureInfo.InvariantCulture);
            if (content.Distinct().Count() < 2)
            {
                // "1984 orwell", "the hobbit 1937": with < 2 other words the number is probably (part of) the title, so
                // it stays and is sent. IN-044: it is soft (optional for title exactness) when a real word remains;
                // when the year is all there is ("1984"), it is the title.
                var withYears = all.Where((t, i) => IsContent(t) && !dropped.Contains(i)).ToList();
                if (content.Count > 0) soft = new HashSet<string>(withYears.Where(IsYear));
                content = withYears;
                year = null;
            }
        }

        var optional = new HashSet<string>(all.Where(t => !content.Contains(t)));
        if (content.Count == 0)
        {
            // "the it": keep every non-year token (or every token) rather than send nothing.
            content = [.. all.Where(t => !IsYear(t))];
            if (content.Count == 0) content = [.. all];
        }

        var orAt = IndexOfOr(all);
        var head = orAt > 0 ? all.Take(orAt).Where(content.Contains).ToList() : [];
        // Repeated words are sent once (IN-011), after numerals so "twenty-six sixty-six" still reads 2666.
        var search = Distinct(Numerals.ToDigitsForSearch(content));

        return new CleanedQuery
        {
            Raw = sanitized,
            Text = text,
            All = all,
            Search = search,
            Optional = optional,
            Head = head,
            Year = year,
            SoftTokens = soft,
            Intents = Vocabulary.Intents(all),
            RemovedNoise = Distinct(all.Where(t => Vocabulary.Noise.Contains(t) && !search.Contains(t))),
            RequestTokens = new HashSet<string>(removed.Select(i => all[i]).Where(t => !content.Contains(t))),
            Topic = request.Topic,
            TopicPreferences = request.Preferences,
            TopicAuthor = request.TopicAuthor,
        };
    }

    /// <summary>The year rule's token shape: <c>^(1\d{3}|20\d{2})$</c> (ASCII digits).</summary>
    public static bool IsYear(string token) => YearToken().IsMatch(token);

    private static bool IsContent(string t) => !Vocabulary.Stop.Contains(t) && !Vocabulary.Noise.Contains(t);

    /// <summary>
    /// Token positions that are never content (they stay in <see cref="CleanedQuery.Optional"/>):
    /// <list type="bullet">
    /// <item>ASCII tokens over <see cref="MaxTokenLength"/> letters (IN-012).</item>
    /// <item>"l"/"d" typed apart from a vowel-initial word ("l etranger camus"): the elided article, as
    /// <see cref="TextFolding.Tokens"/> drops it from "l'étranger" (IN-029).</item>
    /// <item>"jr"/"sr" (IN-065: an unexplained title token otherwise).</item>
    /// <item>A number 1–20 right after "book"/"books" (removed as noise) or after "#" ("hunger games book 2", "dune #1"): sent
    /// as an AND term it zeroes the search (IN-057); <see cref="Vocabulary.HasOrdinal"/> still sees it.</item>
    /// </list>
    /// </summary>
    private static HashSet<int> DroppedPositions(IReadOnlyList<string> all, string text)
    {
        var hashNumbers = Vocabulary.HashNumber().Matches(text)
            .Select(m => m.Value.TrimStart('#').Trim().TrimStart('0'))
            .ToHashSet();
        var dropped = new HashSet<int>();
        for (var i = 0; i < all.Count; i++)
        {
            var t = all[i];
            var next = i + 1 < all.Count ? all[i + 1] : null;
            var prev = i > 0 ? all[i - 1] : null;
            if (t.Length > MaxTokenLength && t.All(char.IsAscii)) dropped.Add(i);
            else if (t is "l" or "d" && next is { Length: > 1 } && "aeiou".Contains(next[0])) dropped.Add(i);
            else if (NameSuffixes.Contains(t)) dropped.Add(i);
            else if (Vocabulary.IsSmallNumber(t)
                     && (prev is "book" or "books"
                         || hashNumbers.Contains(t.TrimStart('0')))) dropped.Add(i);
        }
        return dropped;
    }

    /// <summary>The first bare "or" that is not the first token (subtitle variant "the hobbit or there and back again").</summary>
    private static int IndexOfOr(IReadOnlyList<string> all)
    {
        // A leading "or" makes the head empty, even when another "or" follows.
        if (!all.Skip(1).Contains("or")) return -1;
        return all.ToList().IndexOf("or");
    }

    private static List<string> Distinct(IEnumerable<string> tokens) => [.. tokens.Distinct()];

    [GeneratedRegex(@"^(?:1[0-9]{3}|20[0-9]{2})$")]
    private static partial Regex YearToken();
}
