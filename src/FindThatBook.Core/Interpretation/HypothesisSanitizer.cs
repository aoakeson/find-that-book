using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Interpretation;

/// <summary>
/// Makes LLM output safe to steer retrieval: trimmed, bounded, no field swaps. It never invents anything; it only
/// drops, splits or reorders what the model returned. Runs on every read (cache hits included), so a cached bad parse
/// is re-sanitized too (SY-28).
/// </summary>
public static partial class HypothesisSanitizer
{
    public const int MaxListItems = 8;
    public const int MaxTitleChars = 200;
    public const int MaxAuthorChars = 120;
    public const int MaxCharacterChars = 80;
    public const int MaxKeywordChars = 60;
    public const int MaxSubjects = 3;
    public const int MaxSubjectChars = 40;
    public const int MaxSuggestions = 5;
    public const int MaxSuggestionTitleChars = 120;

    public static QueryHypothesis Sanitize(QueryHypothesis raw, CleanedQuery query)
    {
        var author = PrimaryAuthor(Clean(raw.Author, MaxAuthorChars));
        var title = Clean(raw.Title, MaxTitleChars);
        var original = Clean(raw.OriginalTitle, MaxTitleChars);
        var english = Clean(raw.EnglishTitle, MaxTitleChars);

        // No title but another-language title: search with it (original first).
        if (title is null && (original ?? english) is { } promoted)
        {
            title = promoted;
            if (original is not null) original = null;
            else english = null;
        }

        // A publication year the cleaner extracted is not a title ("... deluxe 1937" → title "1937"); a number the
        // cleaner kept as content is ("1984 orwell").
        if (title is not null && YearOnly(title) is { } year && query.Year == year) title = null;
        // AI-04: "mark huckleberry" → title "Mark Twain" (the author copied into the title).
        if (title is not null && author is not null && Key(title) == Key(author)) title = null;

        if (original is not null && title is not null && Key(original) == Key(title)) original = null;
        if (english is not null && title is not null && Key(english) == Key(title)) english = null;
        if (english is not null && original is not null && Key(english) == Key(original)) english = null;

        var rawTokens = Tokens(query.Raw);
        var rawKey = Key(query.Raw);

        // Topic requests ("find me a book about bears"): subjects and suggestions, and no guessed specifics. A single
        // guessed title must not turn "any book about bears" into a search for one book, and a guessed author must not
        // narrow the topic search to that author. Anything but a clear, typed topic request is a book request: measured,
        // the model also labelled "dickens", "mark twain", "hermione" and a one-book plot as topics.
        var topic = raw.Intent == QueryIntent.Topic && HasTopicSignal(query, rawTokens);
        List<string> subjects = [];
        List<BookSuggestion> suggestions = [];
        if (topic)
        {
            // A heading that is just the author ("charles dickens", "Dickens, Charles, 1812-1870") is not a topic.
            var authorWords = AuthorWords(raw.Author, query.TopicAuthor);
            subjects = Subjects(raw.Subjects, TypedTopicWords(query, rawTokens), authorWords);
            // Suggestions must fit a subject: for "recommend me a good book" (measured: subjects books, reading,
            // bestsellers + five classics) they would be the model's taste, not anything the patron asked for.
            suggestions = subjects.Count > 0 ? Suggestions(raw.Suggestions, subjects) : [];
            // Nothing usable left and no topic phrase typed: treat it as the book search it most likely was.
            if (subjects.Count == 0 && suggestions.Count == 0 && query.Topic.Count == 0 && query.RequestTokens.Count == 0) topic = false;
        }
        if (topic)
        {
            if (title is not null && !Typed(title, rawKey, rawTokens) && !suggestions.Any(x => Key(x.Title) == Key(title)))
            {
                title = null;
                english = null;
                original = null;
            }
            if (author is not null && !Mentioned(author, rawKey, rawTokens)) author = null;
        }
        else
        {
            subjects = [];
            suggestions = [];
        }

        var titleKey = title is null ? null : Key(title);
        var authorKey = author is null ? null : Key(author);
        var characters = CleanList(raw.Characters, MaxCharacterChars)
            // AI-20: "harry potter" → characters ["harry potter"] would waste a person: call on the title itself.
            .Where(c => Key(c) != titleKey && Key(c) != authorKey)
            // AI-04: measured, gpt-4.1-mini added Bilbo, Gandalf and Thorin to "tolkien hobbit illustrated deluxe".
            .Where(c => Mentioned(c, rawKey, rawTokens))
            .Take(MaxListItems)
            .ToList();
        var keywords = CleanList(raw.Keywords, MaxKeywordChars).Take(MaxListItems).ToList();

        return new QueryHypothesis(title, author, characters, keywords, english, original)
        {
            Intent = topic ? QueryIntent.Topic : QueryIntent.Book,
            Subjects = subjects,
            Suggestions = suggestions,
        };
    }

    /// <summary>
    /// Subject headings as search terms: lower case, letters/digits/space/comma/hyphen/parentheses only (anything else
    /// becomes a space, so injected sentences fall apart into words), ≤ 40 chars, ≤ 3, no duplicates, no generic ones.
    /// </summary>
    private static List<string> Subjects(IReadOnlyList<string>? raw, HashSet<string> typedTopic, HashSet<string> authorWords)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (raw ?? []).Take(MaxSubjects * 4))
        {
            if (Clean(item, MaxSubjectChars * 4) is not { } cleaned) continue;
            var sb = new StringBuilder(cleaned.Length);
            foreach (var rune in cleaned.EnumerateRunes())
            {
                var keep = Rune.IsLetterOrDigit(rune) || rune.Value is ' ' or ',' or '-' or '(' or ')';
                sb.Append(keep ? Rune.ToLowerInvariant(rune).ToString() : " ");
            }
            var subject = Clean(sb.ToString(), MaxSubjectChars)?.Trim(' ', ',', '-');
            if (string.IsNullOrEmpty(subject) || !subject.Any(char.IsLetter) || IsGeneric(subject, typedTopic) || IsAuthorName(subject, authorWords)
                || !seen.Add(Key(subject))) continue;
            result.Add(subject);
            if (result.Count == MaxSubjects) break;
        }
        return result;
    }

    /// <summary>Plural or collective book nouns (and a few non-English ones the English-only cleaner can't frame).</summary>
    private static readonly HashSet<string> CollectiveNouns = new(StringComparer.Ordinal)
    {
        "books", "novels", "stories", "poems", "reads", "series", "anything", "something",
        "livre", "livres", "libro", "libros", "livro", "livros", "buch", "bücher", "romans", "romane", "novela", "novelas",
    };

    private static readonly HashSet<string> LongTopicWords = new(StringComparer.Ordinal)
    {
        "about", "regarding", "concerning", "featuring", "involving",
    };

    /// <summary>
    /// A topic request is something the patron typed, not only the model's label: the cleaner's topic frame or request
    /// phrase, a plural/collective book noun, "like"/"similar", or "recommend"/"suggest"; for longer text (more than 3 content words) also
    /// "about", "regarding", "set in"… ("the purple elephant who sang opera on mars" has none of them: a book search).
    /// </summary>
    private static bool HasTopicSignal(CleanedQuery query, IReadOnlyList<string> rawTokens)
    {
        if (query.Topic.Count > 0 || query.RequestTokens.Count > 0) return true;
        if (rawTokens.Any(t => CollectiveNouns.Contains(t) || t is "like" or "similar" or "recommend" or "suggest"
                || t.StartsWith("recommendation", StringComparison.Ordinal))) return true;
        if (query.Search.Count <= 3) return false;
        for (var i = 0; i < rawTokens.Count; i++)
            if (LongTopicWords.Contains(rawTokens[i]) || (rawTokens[i] == "set" && i + 1 < rawTokens.Count && rawTokens[i + 1] == "in"))
                return true;
        return false;
    }

    private static HashSet<string> AuthorWords(string? interpreterAuthor, string? typedAuthor) =>
        Tokens($"{interpreterAuthor} {typedAuthor}").Where(w => w.Any(char.IsLetter)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every word with letters is a word of the author's name (dates and commas aside).</summary>
    private static bool IsAuthorName(string subject, HashSet<string> authorWords)
    {
        var words = Tokens(subject).Where(w => w.Any(char.IsLetter)).ToList();
        return authorWords.Count > 0 && words.Count > 0 && words.All(authorWords.Contains);
    }

    /// <summary>Words that fit nearly every book: a heading made only of them says nothing about the request.</summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "book", "reading", "read", "fiction", "novel", "story", "stories", "literature", "bestseller", "seller", "classic",
        "good", "great", "best", "popular", "general", "reader", "recommendation",
    };

    private static readonly HashSet<string> HeadingFillers = new(StringComparer.Ordinal) { "and", "of", "the" };

    /// <summary>Request words that frame a topic rather than name one ("a good book about …").</summary>
    private static readonly HashSet<string> FrameWords = new(StringComparer.Ordinal)
    {
        "book", "novel", "story", "stories", "read", "something", "anything", "good", "great", "best", "nice", "popular", "title",
    };

    /// <summary>
    /// "books and reading", "fiction", "best books" are generic (measured: Gemini answered "recommend me a good book" with
    /// subject "books and reading"); kept only when the patron typed one of those words as the topic ("books about reading").
    /// </summary>
    private static bool IsGeneric(string subject, HashSet<string> typedTopic)
    {
        var words = Tokens(subject).Where(w => !HeadingFillers.Contains(w)).Select(Singular).ToList();
        return words.Count > 0 && words.All(GenericWords.Contains) && !words.Any(typedTopic.Contains);
    }

    private static readonly HashSet<string> TopicPrepositions = new(StringComparer.Ordinal)
    {
        "about", "on", "regarding", "concerning", "featuring", "involving",
    };

    /// <summary>
    /// The topic words the patron typed: the cleaner's topic; else the words after the last "about"/"on"/…
    /// ("a novel about novels" → novels); else every typed word that is not request framing.
    /// </summary>
    private static HashSet<string> TypedTopicWords(CleanedQuery query, IReadOnlyList<string> rawTokens)
    {
        IEnumerable<string> words;
        if (query.Topic.Count > 0)
        {
            words = query.Topic.SelectMany(Tokens);
        }
        else
        {
            var at = rawTokens.Select((w, i) => (w, i)).LastOrDefault(t => TopicPrepositions.Contains(t.w), (w: "", i: -1)).i;
            words = at >= 0 ? rawTokens.Skip(at + 1) : rawTokens.Where(w => !FrameWords.Contains(Singular(w)));
        }
        return words.Select(Singular).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>"books" → "book", "stories" stays (listed as is); enough for comparing short heading words.</summary>
    private static string Singular(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && word != "stories" ? word[..^1] : word;

    /// <summary>Suggested books: a title is required (≤ 120 chars); the author is optional; ≤ 5, no duplicates, none that is just a subject.</summary>
    private static List<BookSuggestion> Suggestions(IReadOnlyList<BookSuggestion>? raw, List<string> subjects)
    {
        var subjectKeys = subjects.Select(Key).ToHashSet(StringComparer.Ordinal);
        var result = new List<BookSuggestion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (raw ?? []).Take(MaxSuggestions * 4))
        {
            if (Clean(item?.Title, MaxSuggestionTitleChars) is not { } title) continue;
            var key = Key(title);
            if (key.Length == 0 || subjectKeys.Contains(key) || !seen.Add(key)) continue;
            result.Add(new BookSuggestion(title, PrimaryAuthor(Clean(item!.Author, MaxAuthorChars))));
            if (result.Count == MaxSuggestions) break;
        }
        return result;
    }

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "a", "an", "in", "on", "to", "for", "with", "by", "at",
    };

    /// <summary>The patron typed this title: as a substring, or every significant word of it.</summary>
    private static bool Typed(string title, string rawKey, IReadOnlyList<string> rawTokens)
    {
        var key = Key(title);
        if (key.Length > 0 && rawKey.Contains(key, StringComparison.Ordinal)) return true;
        var words = Tokens(title).Where(w => w.Length >= 3 && !TitleStopWords.Contains(w)).ToList();
        return words.Count > 0 && words.All(rawTokens.Contains);
    }

    /// <summary>
    /// The first author of "X and Y" / "X & Y" / "X; Y" / "X with Y": the LLM can return co-authors as one string, and
    /// the surname of the whole string would be the second author's. "Tolkien, J.R.R." becomes "J.R.R. Tolkien".
    /// Role notes ("(editor)", "et al.") and a leading "by" are removed.
    /// </summary>
    public static string? PrimaryAuthor(string? author)
    {
        if (author is null) return null;
        var a = RolePattern().Replace(author, " ");
        a = LeadingByPattern().Replace(a, "");
        a = EtAlPattern().Replace(a, "");
        a = CoAuthorPattern().Split(a)[0];
        var commaParts = a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (commaParts.Length == 2 && WordCount(commaParts[0]) <= 2 && FullWords(commaParts[1]) <= 1)
            a = $"{commaParts[1]} {commaParts[0]}"; // "Surname, Given" / "Le Guin, Ursula K."
        else if (commaParts.Length >= 2)
            a = commaParts[0]; // "Neil Gaiman, Terry Pratchett"
        return Clean(a, MaxAuthorChars);
    }

    /// <summary>
    /// Trim, remove format characters, turn control characters into spaces, collapse spaces, unwrap quotes, treat
    /// "null"/"unknown" as null, cap length.
    /// Works on runes, so supplementary-plane format characters (Unicode TAG characters U+E0000–U+E007F, which can hide
    /// text) are dropped too, not just the BMP ones.
    /// </summary>
    public static string? Clean(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Format) continue; // zero-width, bidi, TAG characters: removed (as the query validator does)
            if (Rune.IsWhiteSpace(rune) || category == UnicodeCategory.Control)
            {
                space = sb.Length > 0;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(rune.ToString());
        }
        var s = sb.ToString().Trim().Trim('"', '\'', '“', '”', '‘', '’', '«', '»').Trim();
        if (s.Length == 0 || s.ToLowerInvariant() is "null" or "none" or "n/a" or "unknown") return null;
        if (s.Length > maxChars)
        {
            var cut = char.IsHighSurrogate(s[maxChars - 1]) ? maxChars - 1 : maxChars;
            s = s[..cut].TrimEnd();
        }
        return s;
    }

    private static IEnumerable<string> CleanList(IReadOnlyList<string>? items, int maxChars)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (items ?? []).Take(MaxListItems * 4))
            if (Clean(item, maxChars) is { } s && seen.Add(Key(s)))
                yield return s;
    }

    /// <summary>Lower-case letters and digits only: "J.R.R. Tolkien" and "jrr tolkien" compare equal.</summary>
    internal static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// A character counts only if the patron typed it: as a substring (any script), or one of its words matches a typed
    /// word exactly, as a prefix ("huck" → Huckleberry) or with one typo ("hermoine" → Hermione), so the model's
    /// spelling fixes survive but names it added from its own knowledge do not.
    /// </summary>
    private static bool Mentioned(string character, string rawKey, IReadOnlyList<string> rawTokens)
    {
        var key = Key(character);
        if (key.Length > 0 && rawKey.Contains(key, StringComparison.Ordinal)) return true;
        foreach (var word in Tokens(character))
        {
            if (word.Length < 3) continue;
            foreach (var typed in rawTokens)
            {
                if (typed == word || (typed.Length >= 4 && word.StartsWith(typed, StringComparison.Ordinal))) return true;
                if (Math.Min(typed.Length, word.Length) >= 5 && Math.Abs(typed.Length - word.Length) <= 1 && Osa(typed, word) <= 1) return true;
            }
        }
        return false;
    }

    private static List<string> Tokens(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in s + " ")
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
        return tokens;
    }

    /// <summary>Optimal string alignment distance (Levenshtein + adjacent transposition).</summary>
    private static int Osa(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    private static int? YearOnly(string title)
    {
        var m = YearOnlyPattern().Match(title);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static int WordCount(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Words that are not initials ("J.R.R.", "K."): two of them after a comma read as a second person.</summary>
    private static int FullWords(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Length > 2 && !w.EndsWith('.'));

    [GeneratedRegex(@"^(?:c(?:a)?\.?\s*)?([0-9]{3,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex YearOnlyPattern();

    [GeneratedRegex(@"\s+(?:and|&|with)\s+|\s*[;/]\s*|\s*&\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CoAuthorPattern();

    [GeneratedRegex(@"\((?:ed|eds|editor|editors|author|illustrator|translator|trans|adapter)\.?\)", RegexOptions.IgnoreCase)]
    private static partial Regex RolePattern();

    [GeneratedRegex(@"^\s*by\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingByPattern();

    [GeneratedRegex(@",?\s+et\.?\s+al\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EtAlPattern();
}
