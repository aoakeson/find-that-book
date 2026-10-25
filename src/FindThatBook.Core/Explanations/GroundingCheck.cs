using System.Text;
using System.Text.Json;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Explanations;

/// <summary>Why a sentence failed the check (empty when it passed). Codes are stable for tests and the eval.</summary>
public sealed record GroundingResult(IReadOnlyList<string> Problems)
{
    public bool Passed => Problems.Count == 0;
}

/// <summary>
/// Decides whether an LLM sentence may be shown for a candidate. Pure and strict: a false rejection costs only a
/// template sentence, a false acceptance shows the user something Open Library never said.
/// <list type="number">
/// <item>Shape: one plain sentence, ≤ 240 characters, no markup, URLs, line breaks, "!" or "?" (AI-29, AI-31).
/// Initials and abbreviations ("J.R.R.", "C. S.", "Dr.") never end a sentence (AI-28).</item>
/// <item>Closed vocabulary: every word comes from the facts or a small list of explanation words, so injected claims
/// ("free download at hobbitpdf dot ru", "pirated", "banned") and internal jargon ("tier c") fail (AI-29, AI-32).
/// Fact strings are matched first, ignoring case, accents and spacing, so titles like "Complete Poems, 1904-1962",
/// names with initials and character names pass (AI-28). A title counts only when quoted whole: its words are not free
/// vocabulary, so a crafted catalog title cannot supply words for a new claim.</item>
/// <item>Numbers: only the year (present only when the typed year matched, DA-061), the edition count, the duplicate
/// count or numbers inside a fact string.</item>
/// <item>Relations: a contributor called the author, a primary author called a contributor, a wrong role, a role or
/// authorship given to a character, or a negated claim about a person fail (AI-36).</item>
/// <item>Meaning: the sentence states the code's match kind and contradicts none of it (no "exact" for a near match,
/// AI-32), names a primary author, names the matched contributor with their role (tier B), and names a matched
/// character when Open Library lists one.</item>
/// </list>
/// </summary>
public static partial class GroundingCheck
{
    public const int MaxLength = 240;

    public static GroundingResult Check(string? sentence, ExplanationFacts facts)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return new(["empty"]);
        var text = sentence.Trim();
        var problems = new List<string>();
        if (text.Length > MaxLength) problems.Add("too_long");

        var model = FactModel.From(facts);
        CheckCharacters(text, model, problems);
        var tokens = Tokenizer.Read(text, model, problems);
        CheckShape(tokens, problems);
        CheckLabel(tokens, model, problems);
        CheckVocabulary(tokens, model, problems);
        CheckNumbers(tokens, model, problems);
        var relations = Relations.Analyze(tokens, model, problems);
        CheckMeaning(tokens, model, problems);
        CheckRequired(relations, model, problems);
        return new(problems.Distinct(StringComparer.Ordinal).ToList());
    }

    // ---------------------------------------------------------------- word lists

    /// <summary>Words an explanation may use besides the facts' own words (~150, deliberately closed).</summary>
    private static readonly HashSet<string> ExplanationWords =
    [
        "a", "an", "the", "and", "or", "of", "by", "as", "at", "in", "on", "for", "with", "to", "from", "via", "per", "under",
        "into", "its", "it", "this", "that", "these", "those", "which", "who", "whose", "is", "are", "was", "were", "be",
        "been", "being", "has", "have", "had", "also", "not", "no", "only", "both", "each", "other", "another", "different",
        "same", "but", "while", "plus", "than", "his", "her", "their", "here",
        "exact", "exactly", "title", "titles", "titled", "match", "matches", "matched", "matching", "near", "nearly",
        "close", "closely", "partial", "partially", "similar", "interpreted", "inferred", "interpretation", "original",
        "originally", "language",
        "author", "authors", "authored", "primary", "primarily", "main", "co", "wrote", "written", "writer", "writers",
        "credited", "credit", "credits", "listed", "lists", "list", "listing", "named", "called", "name", "names",
        "alternative", "alternate",
        "work", "works", "book", "books", "edition", "editions", "version", "versions", "record", "records", "duplicate",
        "duplicates", "merged", "catalog", "catalogue", "first", "published", "publication", "publish", "year", "years",
        "date", "open", "library", "according",
        "character", "characters", "appears", "appear", "appearing", "features", "featuring", "featured", "includes",
        "including", "include", "contains", "containing", "contain", "term", "terms", "word", "words", "search", "searched",
        "query", "requested", "request", "asked", "weak", "loose", "loosely", "result", "results", "returned", "signal", "lower",
        "topic", "subject", "subjects", "suggested",
    ];

    private static readonly HashSet<string> Abbreviations =
        ["dr", "mr", "mrs", "ms", "st", "jr", "sr", "vs", "vol", "vols", "no", "nos", "ed", "eds", "prof", "rev", "mt", "ft", "co", "inc", "ltd", "pp", "ch"];

    private static readonly HashSet<string> NameParticles =
        ["de", "del", "della", "der", "den", "di", "da", "du", "des", "la", "le", "van", "von", "y", "e", "el", "al", "bin", "ibn", "ben", "mac", "st", "ter", "ten", "zu"];

    private static readonly HashSet<string> AuthorWords = ["author", "authors", "authored", "wrote", "written", "primary", "primarily"];
    private static readonly HashSet<string> WriterWords = ["writer", "writers"];
    private static readonly HashSet<string> Negations = ["not", "no"];
    private static readonly HashSet<string> BoundaryWords = ["and", "with", "while", "plus", "but", "whereas"];
    private static readonly HashSet<string> Linkers =
        ["is", "are", "was", "were", "be", "been", "being", "listed", "as", "the", "a", "an", "also", "credited", "its", "their", "his", "her", "named", "called", "main", "here"];
    private static readonly HashSet<string> MatchVerbs = ["matched", "match", "matches", "matching", "returned", "listed", "sorted", "ranked"];
    private static readonly HashSet<string> AllowedPunctuation =
        [".", ",", ";", ":", "'", "’", "‘", "\"", "“", "”", "(", ")", "-", "‐", "‑", "–", "—", "&"];

    // ---------------------------------------------------------------- shape, words, numbers

    private static void CheckCharacters(string s, FactModel model, List<string> problems)
    {
        foreach (var rune in s.EnumerateRunes())
        {
            if (ExplanationText.IsWordRune(rune) || rune.Value is ' ' or 0xA0) continue;
            var ch = rune.ToString();
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune)) problems.Add("line_break_or_control");
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(rune.Value) == System.Globalization.UnicodeCategory.Format) problems.Add("format_character");
            else if (!AllowedPunctuation.Contains(ch) && !model.FactCharacters.Contains(ch)) problems.Add($"disallowed_character:{ch}");
        }
    }

    private static void CheckShape(List<Token> tokens, List<string> problems)
    {
        for (var k = 0; k < tokens.Count; k++)
        {
            if (tokens[k].Kind != TokenKind.Period) continue;
            if (tokens[k].UrlLike) problems.Add("url_like");
            if (k == tokens.Count - 1) continue;
            var prev = k > 0 ? tokens[k - 1] : null;
            var initial = prev is { Kind: TokenKind.Word } && prev.Folded.Length == 1;
            var abbreviation = prev is { Kind: TokenKind.Word } && Abbreviations.Contains(prev.Folded);
            if (!initial && !abbreviation && !tokens[k].UrlLike) problems.Add("multiple_sentences");
        }
    }

    /// <summary>The edition title must follow its own label, not another "… as" ("also known in English as also published as X").</summary>
    private static void CheckLabel(List<Token> tokens, FactModel model, List<string> problems)
    {
        for (var k = 1; k < tokens.Count; k++)
            if (tokens[k].Kind == TokenKind.Fact && model.Texts[tokens[k].Fact].Kind == FactKind.AlsoKnownAs
                && tokens[k - 1] is { Kind: TokenKind.Word, Folded: "as" })
                problems.Add("garbled_label");
    }

    private static void CheckVocabulary(List<Token> tokens, FactModel model, List<string> problems)
    {
        foreach (var t in tokens.Where(t => t.Kind == TokenKind.Word))
        {
            var word = StripPossessive(t.Folded);
            if (word.Length == 0 || Allowed(word, model)) continue;
            var parts = word.Split(['\'', '’', '-', '‐', '‑'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(p => Allowed(p, model))) continue;
            var role = parts.FirstOrDefault(p => RoleLexicon.CategoryOfWord(p) is not null && !Allowed(p, model));
            problems.Add(role is not null ? $"role_not_in_facts:{role}" : $"unknown_word:{word}");
        }
    }

    private static bool Allowed(string word, FactModel model)
    {
        if (model.Vocabulary.Contains(word) || ExplanationWords.Contains(word)) return true;
        var f = model.Facts;
        var category = RoleLexicon.CategoryOfWord(word);
        if (category is not null)
            return category == RoleLexicon.Generic || model.Roles.Contains(category)
                || (category == "adapter" && f.Intent == EditionIntent.Adaptation && word is "adaptation" or "adaptations" or "adapted");
        return f.Intent == EditionIntent.StudyGuide && word is "study" or "guide";
    }

    private static void CheckNumbers(List<Token> tokens, FactModel model, List<string> problems)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != TokenKind.Number) continue;
            var number = TrimZeros(t.Folded);
            var before = tokens.Skip(Math.Max(0, i - 3)).Take(Math.Min(3, i)).Where(x => x.Kind == TokenKind.Word).Select(x => x.Folded).ToHashSet();
            var after = tokens.Skip(i + 1).Take(2).Where(x => x.Kind == TokenKind.Word).Select(x => x.Folded).ToHashSet();
            var expected = after.Overlaps(["edition", "editions", "version", "versions"])
                ? model.Facts.EditionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : after.Overlaps(["duplicate", "duplicates", "record", "records"])
                    ? model.Facts.DuplicateRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : before.Overlaps(["published", "publication", "year", "date"]) || after.Overlaps(["year", "years"])
                        ? model.Facts.FirstPublishYear?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null;
            if (expected is not null ? number != expected :
                (before.Overlaps(["published", "publication", "year", "date"]) || after.Overlaps(["year", "years"])) || !model.Numbers.Contains(number))
                problems.Add($"number_not_in_facts:{t.Text}");
        }
    }

    // ---------------------------------------------------------------- the code's match meaning

    private static readonly string[] NearWords = ["near", "nearly", "close", "closely", "partial", "partially", "similar"];
    private static readonly string[] ExactWords = ["exact", "exactly"];
    private static readonly string[] WeakWords = ["weak", "loose", "loosely"];
    private static readonly HashSet<string> MatchWords =
    [
        "match", "matches", "matched", "matching", "exact", "exactly", "near", "nearly", "close", "closely", "partial", "partially",
        "similar", "interpreted", "inferred", "original", "originally", "character", "characters", "title", "titles", "weak",
        "loose", "loosely", "author-only", "contributor-only", "title-only", "topic", "subject", "subjects", "suggested",
    ];
    private static readonly string[] TopicWords = ["topic", "subject", "subjects", "suggested"];

    private static void CheckMeaning(List<Token> tokens, FactModel model, List<string> problems)
    {
        var sequence = tokens.Where(t => t.Kind == TokenKind.Word).Select(t => StripPossessive(t.Folded)).ToList();
        var words = new HashSet<string>(sequence, StringComparer.Ordinal);
        foreach (var w in sequence) words.UnionWith(w.Split(['-', '‐', '‑'], StringSplitOptions.RemoveEmptyEntries));
        var authorOnly = sequence.Contains("author-only") || sequence.Zip(sequence.Skip(1)).Any(p => p is ("author", "only"));
        var contributorOnly = sequence.Contains("contributor-only") || sequence.Zip(sequence.Skip(1)).Any(p => p is ("contributor", "only"));
        var titleMatchClaim = Segments(tokens).Any(s => s.Overlaps(["title", "titles"]) && s.Overlaps(["match", "matches", "matched", "matching"]));
        // "No exact title match; Tolkien is the primary author" states the opposite of the code's match.
        // TitleOnly's own "but not by the searched author" has no match word in its clause, so it still passes.
        if (Segments(tokens).Any(s => s.Overlaps(Negations) && s.Overlaps(MatchWords))) problems.Add("negated_match");

        var kind = model.Facts.Kind;
        string[] required;
        IEnumerable<string> forbidden;
        var forbidAuthorOnly = true;
        switch (kind)
        {
            case MatchKind.ExactTitle:
                (required, forbidden) = (ExactWords, NearWords.Concat(WeakWords));
                break;
            case MatchKind.InferredTitle:
                (required, forbidden) = (["interpreted", "inferred", "interpretation"], NearWords.Concat(WeakWords));
                break;
            case MatchKind.OriginalTitle:
                (required, forbidden) = (["original", "originally"], NearWords.Concat(WeakWords));
                break;
            case MatchKind.NearTitle:
                (required, forbidden) = (NearWords, ExactWords.Concat(WeakWords));
                break;
            case MatchKind.AuthorOnly:
                (required, forbidden) = (["author", "authors"], ExactWords.Concat(NearWords).Concat(WeakWords));
                forbidAuthorOnly = false;
                if (titleMatchClaim) problems.Add("match_meaning_contradicted:title_match");
                break;
            case MatchKind.ContributorOnly:
                var role = model.MatchedContributor >= 0 ? model.Persons[model.MatchedContributor].Roles : [];
                (required, forbidden) = (["contributor", "contributors", "contributor-only", .. role.SelectMany(RoleLexicon.WordsFor)], ExactWords.Concat(NearWords).Concat(WeakWords));
                if (titleMatchClaim) problems.Add("match_meaning_contradicted:title_match");
                break;
            case MatchKind.Character:
                (required, forbidden) = (["character", "characters"], ExactWords.Concat(WeakWords));
                break;
            case MatchKind.TitleOnly:
                (required, forbidden) = (["title", "titles"], NearWords.Concat(WeakWords));
                if (!words.Overlaps(["not", "different", "other", "another"])) problems.Add("match_meaning_missing:title_only");
                break;
            case MatchKind.PartialTitle:
                (required, forbidden) = (["partial", "partially", "contains", "containing", "term", "terms", "word", "words"], ExactWords.Concat(WeakWords));
                break;
            case MatchKind.Topic:
                (required, forbidden) = (["topic", "subject", "subjects"], ExactWords.Concat(NearWords).Concat(WeakWords));
                // The subjects are the evidence: they must be quoted as Open Library lists them.
                if (!tokens.Any(t => t.Kind == TokenKind.Fact && model.Texts[t.Fact].Kind == FactKind.Subject)) problems.Add("subject_missing");
                CheckSubjectLists(tokens, model, problems);
                break;
            case MatchKind.Suggested:
                // No subject evidence: the sentence may say it was suggested, never what the book is about.
                (required, forbidden) = (["suggested"], ExactWords.Concat(NearWords).Concat(WeakWords).Concat(["subject", "subjects"]));
                if (sequence.Zip(sequence.Skip(1)).Any(p => p is ("topic", "match" or "matches" or "matched")))
                    problems.Add("match_meaning_contradicted:topic_match");
                break;
            default:
                (required, forbidden) = (WeakWords, ExactWords.Concat(NearWords).Concat(["character", "characters"]));
                break;
        }
        if (kind is not (MatchKind.Topic or MatchKind.Suggested)) forbidden = forbidden.Concat(TopicWords);

        if (!words.Overlaps(required)) problems.Add($"match_meaning_missing:{JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString())}");
        foreach (var w in forbidden.Where(words.Contains)) problems.Add($"match_meaning_contradicted:{w}");
        if (forbidAuthorOnly && authorOnly) problems.Add("match_meaning_contradicted:author_only");
        if (kind != MatchKind.ContributorOnly && contributorOnly) problems.Add("match_meaning_contradicted:contributor_only");

        if (model.Facts.FirstPublishYear is not null && Segments(tokens).Any(s => s.Overlaps(Negations) && s.Overlaps(["year", "years", "date", "published", "publication"])))
            problems.Add("denies_year");
    }

    /// <summary>Folded words per clause: split at punctuation and at clause words ("but", "and", "with"…), as the relation check does.</summary>
    private static IEnumerable<HashSet<string>> Segments(List<Token> tokens)
    {
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tokens)
        {
            if (t.Kind is TokenKind.Boundary or TokenKind.Period || (t.Kind == TokenKind.Word && BoundaryWords.Contains(t.Folded)))
            {
                if (current.Count > 0) yield return current;
                current = new HashSet<string>(StringComparer.Ordinal);
                continue;
            }
            if (t.Kind == TokenKind.Word) current.Add(StripPossessive(t.Folded));
        }
        if (current.Count > 0) yield return current;
    }

    private static readonly HashSet<string> SubjectListLead = ["the", "include", "includes", "including", "are", "is", "of", "as"];

    /// <summary>
    /// "lists the subjects X and Y": every listed item must be a subject fact. A title or a name is an allowed fact
    /// elsewhere, but not a subject ("the subjects Bears and Winnie-the-Pooh" fails).
    /// </summary>
    private static void CheckSubjectLists(List<Token> tokens, FactModel model, List<string> problems)
    {
        for (var k = 0; k < tokens.Count; k++)
        {
            if (tokens[k] is not { Kind: TokenKind.Word, Folded: "subject" or "subjects" }) continue;
            var j = k + 1;
            while (j < tokens.Count && (tokens[j] is { Kind: TokenKind.Word } w && SubjectListLead.Contains(w.Folded) || tokens[j] is { Kind: TokenKind.Boundary, Folded: ":" })) j++;
            var items = 0;
            while (j < tokens.Count)
            {
                if (tokens[j].Kind != TokenKind.Fact || model.Texts[tokens[j].Fact].Kind != FactKind.Subject)
                {
                    problems.Add("subject_list_invalid");
                    return;
                }
                items++;
                j++;
                // Another item follows only after "," / "and" / "&"; anything else ends the list.
                if (j < tokens.Count && (tokens[j] is { Kind: TokenKind.Word, Folded: "and" } || tokens[j] is { Kind: TokenKind.Boundary, Folded: "," or "and" })
                    && j + 1 < tokens.Count && tokens[j + 1].Kind == TokenKind.Fact)
                {
                    j++;
                    continue;
                }
                break;
            }
            if (items == 0) problems.Add("subject_list_invalid");
        }
    }

    private static void CheckRequired(Relations relations, FactModel model, List<string> problems)
    {
        if (model.Facts.PrimaryAuthors.Count > 0 && !relations.PrimaryMentioned) problems.Add("primary_author_missing");
        if (model.MatchedContributor >= 0 && !relations.RoleConfirmed.Contains(model.MatchedContributor)) problems.Add("matched_contributor_role_missing");
        // The typed character names are often the only reason for the result ("scout atticus" → To Kill a Mockingbird).
        if (model.Facts.MatchedCharacters.Count > 0 && !relations.CharacterMentioned) problems.Add("matched_character_missing");
    }

    // ---------------------------------------------------------------- helpers

    private static string StripPossessive(string word)
    {
        foreach (var suffix in new[] { "'s", "’s" })
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length > suffix.Length) return word[..^suffix.Length];
        return word.TrimEnd('\'', '’');
    }

    private static string TrimZeros(string number)
    {
        var trimmed = number.TrimStart('0');
        return trimmed.Length == 0 || trimmed[0] == '.' ? "0" + trimmed : trimmed;
    }
}
