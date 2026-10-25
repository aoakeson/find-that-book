using System.Text;
using System.Text.RegularExpressions;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Retrieval;

/// <summary>
/// Builds the actual search.json requests: what text is sent, short-query routing (DE-05), Solr syntax safety,
/// person:/OR syntax, the LLM's own words for hypothesis titles.
/// <para>
/// Syntax safety (DA-065..068): every value is rebuilt from word tokens (letters, marks, digits) joined by single
/// spaces, so user or LLM text can never carry Solr syntax: no parentheses, quotes, colons (fielded injection like
/// "title:(hobbit"), leading "-" (negation: "-tolkien", "Spider-Man"), wildcards ("*", "hob*"), boosts, ranges or
/// escapes. Tokens are lowercase, and Open Library treats lowercase and/or/not as plain words (DA-068), so a typed
/// "OR"/"NOT" is a word too. The only syntax ever sent is what this class adds: <c>person:( … )</c>,
/// <c>subject:( … )</c>, <c>title:( … )</c> and upper-case <c> AND </c>/<c> OR </c> joins.
/// </para>
/// </summary>
public static partial class CatalogQueries
{
    /// <summary>
    /// Open Library answers q= shorter than 3 characters with HTTP 422 "Query too short" (DE-05, verified live 10/24:
    /// q=三体, q=ab, q=v.). It counts the raw q value in code points, spaces included, so this does too.
    /// </summary>
    public const int MinQueryCharacters = 3;

    public const int DefaultLimit = 20;
    public const int AuthorWorksLimit = 10;
    public const int MaxPersonTerms = 3;
    public const int MaxAnyOfTerms = 8;
    public const int MaxSubjectTerms = 3;
    public const int MaxSubjectWords = 4;
    public const int MaxSuggestions = 5;

    /// <summary>Call 1. q=&lt;search tokens&gt;, or title= when the text is shorter than 3 characters (三体, it, us, v.).</summary>
    /// <exception cref="ArgumentException">The query has no search tokens (the API rejects those with no_searchable_terms).</exception>
    public static CatalogRequest Broad(CleanedQuery q)
    {
        var text = Join(q.Search);
        if (text.Length == 0) throw new ArgumentException("The query has no searchable terms.", nameof(q));
        return IsTooShortForQ(text)
            ? new CatalogRequest(CatalogSteps.ShortTitle, new CatalogQuery { Title = text })
            : new CatalogRequest(CatalogSteps.Broad, new CatalogQuery { Q = text });
    }

    /// <summary>
    /// Re-plan after Open Library rejected call 1 (422): the same words as a title= search, never a 5xx. Null when
    /// call 1 was already a title= search (nothing different to try) or there is nothing to send.
    /// </summary>
    public static CatalogRequest? AfterRejected(CleanedQuery q)
    {
        var text = Join(q.Search);
        if (text.Length == 0 || IsTooShortForQ(text)) return null;
        return new CatalogRequest(CatalogSteps.ShortTitle, new CatalogQuery { Title = text });
    }

    /// <summary>
    /// title=&lt;LLM main-title content words, folded, NOT numeral-converted: the LLM's own words ("A Tale of Two
    /// Cities" → "tale two cities")&gt; &amp;author=&lt;LLM author surname&gt;; null if nothing to send. When the LLM
    /// gave no usable title, its original title, else its English title, takes the title's place (and then
    /// <see cref="OriginalTitle(QueryHypothesis)"/> returns null, so the same words aren't sent twice).
    /// Gating (does the hypothesis add words? is it grounded?) is the ladder's job, not this builder's.
    /// </summary>
    public static CatalogQuery? HypothesisTitle(QueryHypothesis h)
    {
        var words = TitleWords(h.Title);
        if (words.Count == 0) words = PromotedWords(h);
        var author = AuthorSurname(h);
        if (words.Count == 0 && author is null) return null;
        return new CatalogQuery { Title = words.Count > 0 ? Join(words) : null, Author = author };
    }

    /// <summary>
    /// title=&lt;original title in the LLM's own words: lowercased, accents and script kept, never folded&gt;
    /// (+ author surname for Latin-script titles only: a non-Latin title is specific enough, and its record may credit
    /// the author only in that script). Null when there is no original title, it has the same key as the title, or it
    /// was promoted into <see cref="HypothesisTitle"/>.
    /// </summary>
    public static CatalogQuery? OriginalTitle(QueryHypothesis h) => OriginalTitle(h, OriginalTitleAuthor.LatinScriptOnly);

    /// <summary>
    /// <see cref="OriginalTitle(QueryHypothesis)"/> with an explicit author rule. R3-0366 ("Алексей Пешков Childhood"
    /// found Tolstoy's Детство because the call had no author) argues for <see cref="OriginalTitleAuthor.Always"/>;
    /// the measured default is <see cref="OriginalTitleAuthor.LatinScriptOnly"/>.
    /// </summary>
    public static CatalogQuery? OriginalTitle(QueryHypothesis h, OriginalTitleAuthor authorRule)
    {
        if (string.IsNullOrWhiteSpace(h.OriginalTitle) || TitleText.Sets(h.OriginalTitle).Main.Count == 0) return null;
        if (TitleWords(h.Title).Count == 0) return null; // promoted into HypothesisTitle
        if (TitleText.Key(h.OriginalTitle) == TitleText.Key(h.Title ?? "")) return null;
        var words = OwnWords(h.OriginalTitle);
        if (words.Count == 0) return null;
        var title = Join(words);
        var author = authorRule switch
        {
            OriginalTitleAuthor.Always => AuthorSurname(h),
            OriginalTitleAuthor.LatinScriptOnly when TextFolding.IsLatinScript(title) => AuthorSurname(h),
            _ => null,
        };
        return new CatalogQuery { Title = title, Author = author };
    }

    /// <summary>
    /// q=person:(a AND b …) or person:(a OR b …): up to 3 distinct name tokens, preferring tokens of ≥ 3 letters (≥ 2
    /// for CJK/Hangul, where a 2-character given name is normal). Shorter tokens are dropped only when a longer one
    /// remains, so a character like "Jo" still searches. Requests the person field.
    /// </summary>
    /// <exception cref="ArgumentException">The tokens contain no word at all.</exception>
    public static CatalogQuery Persons(IReadOnlyList<string> tokens, bool allRequired)
    {
        var terms = PersonTerms(tokens);
        if (terms.Count == 0) throw new ArgumentException("No person name token.", nameof(tokens));
        var joiner = allRequired ? " AND " : " OR ";
        return new CatalogQuery { Q = $"person:({string.Join(joiner, terms)})", IncludePersons = true };
    }

    /// <summary>The tokens <see cref="Persons"/> would send (folded, syntax-free, distinct, at most 3); empty means don't call.</summary>
    public static IReadOnlyList<string> PersonTerms(IReadOnlyList<string> tokens)
    {
        var terms = Terms(tokens).ToList();
        var named = terms.Where(t => t.EnumerateRunes().Count(Rune.IsLetter) >= (IsCjk(t) ? 2 : 3)).ToList();
        return [.. (named.Count > 0 ? named : terms.Where(t => t.EnumerateRunes().Any(Rune.IsLetter))).Take(MaxPersonTerms)];
    }

    /// <summary>q=t1 OR t2 … (≤ 8 distinct tokens): typo and noise rescue. A lone token under 3 characters goes as title=.</summary>
    /// <exception cref="ArgumentException">No usable token.</exception>
    public static CatalogQuery AnyOf(IReadOnlyList<string> tokens)
    {
        var terms = Terms(tokens).Take(MaxAnyOfTerms).ToList();
        if (terms.Count == 0) throw new ArgumentException("No searchable token.", nameof(tokens));
        var q = string.Join(" OR ", terms);
        return IsTooShortForQ(q) ? new CatalogQuery { Title = q } : new CatalogQuery { Q = q };
    }

    /// <summary>author_key=K&amp;sort=editions&amp;limit=10: an author's most-published works. Accepts "OL26320A" or "/authors/OL26320A".</summary>
    /// <exception cref="ArgumentException">Not an Open Library author key.</exception>
    public static CatalogQuery AuthorTopWorks(string authorKey)
    {
        var key = authorKey.Trim();
        if (key.StartsWith("/authors/", StringComparison.Ordinal)) key = key["/authors/".Length..];
        if (!AuthorKey().IsMatch(key)) throw new ArgumentException($"Not an Open Library author key: '{authorKey}'.", nameof(authorKey));
        return new CatalogQuery { AuthorKey = key, Sort = CatalogSort.Editions, Limit = AuthorWorksLimit };
    }

    /// <summary>
    /// author=&lt;name&gt;&amp;sort=editions&amp;limit=10 when the hypothesis names an author not in the pool. The name goes as
    /// its folded name tokens ("Tolkien, J. R. R. 1892-1973" → "j r r tolkien").
    /// </summary>
    /// <exception cref="ArgumentException">The name has no word.</exception>
    public static CatalogQuery AuthorByName(string name)
    {
        var words = PersonNames.Tokens(name);
        if (words.Count == 0) throw new ArgumentException("The author name has no word.", nameof(name));
        return new CatalogQuery { Author = Join(words), Sort = CatalogSort.Editions, Limit = AuthorWorksLimit };
    }

    /// <summary>
    /// q=subject:(w1 AND w2 …): works whose subjects carry every topic word ("bears"; "french revolution" → french AND
    /// revolution), default relevance (sort=editions surfaces incidental subjects), with the subject field requested.
    /// Up to 3 terms of up to 4 words each; stop words dropped. <paramref name="authorHint"/> ("by doris lessing") adds
    /// author=. Null when no word is left.
    /// </summary>
    public static CatalogQuery? Topic(IReadOnlyList<string> terms, string? authorHint)
    {
        var words = SubjectTerms(terms).SelectMany(t => t).Distinct().ToList();
        if (words.Count == 0) return null;
        return new CatalogQuery { Q = $"subject:({string.Join(" AND ", words)})", Author = AuthorHint(authorHint), IncludeSubjects = true };
    }

    private static string? AuthorHint(string? hint)
    {
        IReadOnlyList<string> words = hint is null ? [] : PersonNames.Tokens(hint);
        return words.Count > 0 ? Join(words) : null;
    }

    /// <summary>
    /// q=subject:(a AND b) OR subject:(c): any one topic term, for when <see cref="Topic"/> found nothing. A single
    /// multi-word term falls back to any of its words ("french revolution" → subject:(french) OR subject:(revolution)),
    /// since all of them together already found nothing. The author hint adds author= as in <see cref="Topic"/>. Null
    /// when no word is left.
    /// </summary>
    public static CatalogQuery? TopicAny(IReadOnlyList<string> terms, string? authorHint = null)
    {
        var termWords = SubjectTerms(terms).ToList();
        if (termWords is [{ Count: > 1 } only]) termWords = [.. only.Select(w => (IReadOnlyList<string>)[w])];
        var groups = termWords.Select(t => $"subject:({string.Join(" AND ", t)})").Distinct().ToList();
        if (groups.Count == 0) return null;
        return new CatalogQuery { Q = string.Join(" OR ", groups), Author = AuthorHint(authorHint), IncludeSubjects = true };
    }

    /// <summary>
    /// q=title:(w1 w2) OR title:(…): one call that checks up to 5 interpreter-suggested books exist, each title rebuilt
    /// from its main-title words (author not required), with the subject field requested. Null when no title has a word.
    /// </summary>
    public static CatalogQuery? Suggestions(IReadOnlyList<BookSuggestion> suggestions)
    {
        var groups = suggestions
            .Select(s => TitleWords(s.Title))
            .Where(w => w.Count > 0)
            .Select(w => $"title:({Join(w)})")
            .Distinct()
            .Take(MaxSuggestions)
            .ToList();
        return groups.Count == 0 ? null : new CatalogQuery { Q = string.Join(" OR ", groups), IncludeSubjects = true };
    }

    /// <summary>Each topic term's folded content words (≤ 4), at most 3 non-empty terms.</summary>
    private static IEnumerable<IReadOnlyList<string>> SubjectTerms(IReadOnlyList<string> terms) => terms
        .Select(t => (IReadOnlyList<string>)[.. TextFolding.Content(TextFolding.Tokens(t)).Distinct().Take(MaxSubjectWords)])
        .Where(w => w.Count > 0)
        .Take(MaxSubjectTerms);

    /// <summary>True when Open Library would reject this text as q= (DE-05): under 3 code points.</summary>
    public static bool IsTooShortForQ(string text) => TextFolding.CodePoints(text) < MinQueryCharacters;

    // ---- word sources ----

    /// <summary>Main-title content words (or all main words), folded, number words kept as words.</summary>
    private static IReadOnlyList<string> TitleWords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        var main = TextFolding.Tokens(TitleText.Split(title).Main);
        var content = TextFolding.Content(main);
        return content.Count > 0 ? content : main;
    }

    /// <summary>The LLM's own words of an alternative title: the part before ':'/';', unfolded, minus stop words.</summary>
    private static IReadOnlyList<string> OwnWords(string title)
    {
        var main = MainPart().Split(title)[0];
        var words = TextFolding.UnfoldedTokens(main);
        var content = words.Where(w => !Vocabulary.Stop.Contains(w)).ToList();
        return content.Count > 0 ? content : words;
    }

    private static IReadOnlyList<string> PromotedWords(QueryHypothesis h)
    {
        foreach (var alt in new[] { h.OriginalTitle, h.EnglishTitle })
            if (!string.IsNullOrWhiteSpace(alt) && TitleText.Sets(alt).Main.Count > 0) return OwnWords(alt);
        return [];
    }

    private static string? AuthorSurname(QueryHypothesis h)
    {
        if (string.IsNullOrWhiteSpace(h.Author)) return null;
        var surname = Join([PersonNames.Surname(h.Author)]);
        return surname.Length > 0 ? surname : null;
    }

    /// <summary>Caller tokens re-tokenized (folded word runs only), distinct, in order: nothing else can reach q=.</summary>
    private static IEnumerable<string> Terms(IEnumerable<string> tokens) =>
        tokens.SelectMany(t => TextFolding.Tokens(t)).Distinct();

    /// <summary>Joins word tokens; any token that is not purely word characters is re-split, so the result is always safe.</summary>
    private static string Join(IEnumerable<string> words) =>
        string.Join(' ', words.SelectMany(w => IsWord(w) ? [w] : TextFolding.UnfoldedTokens(w)));

    private static bool IsWord(string w) => w.Length > 0 && w.EnumerateRunes().All(TextFolding.IsWordChar) && w == w.ToLowerInvariant();

    private static bool IsCjk(string t) => t.EnumerateRunes().Any(r => r.Value is >= 0x3040 and <= 0x30FF or >= 0x3400 and <= 0x9FFF
        or >= 0xAC00 and <= 0xD7A3 or >= 0x20000 and <= 0x3FFFF);

    [GeneratedRegex(@"\s*[:;]\s*")]
    private static partial Regex MainPart();

    [GeneratedRegex(@"^OL[0-9]+A$")]
    private static partial Regex AuthorKey();
}

/// <summary>Whether the original-title call carries the hypothesis author's surname (R3-0366).</summary>
public enum OriginalTitleAuthor
{
    /// <summary>Only when every letter of the title is Latin script (the measured default).</summary>
    LatinScriptOnly,
    /// <summary>Always, when the hypothesis has an author (R3-0366: a same-title work by another author must not win).</summary>
    Always,
    /// <summary>Never.</summary>
    Never,
}

/// <summary>Step names, as reported in meta.open_library.steps.</summary>
public static class CatalogSteps
{
    public const string Broad = "broad";
    public const string ShortTitle = "short_title";
    public const string Hypothesis = "hypothesis";
    public const string OriginalTitle = "original_title";
    public const string Person = "person";
    public const string PersonAny = "person_any";
    public const string AnyOf = "any_of";
    public const string PersonFallback = "person_fallback";
    public const string PersonFallbackAny = "person_fallback_any";
    public const string AuthorWorks = "author_works";
    public const string Topic = "topic";
    public const string TopicAny = "topic_any";
    public const string Suggestions = "suggestions";
}
