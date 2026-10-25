using System.Globalization;
using System.Text;

namespace FindThatBook.Core.Explanations;

/// <summary>How the check sees the facts (people, fact strings, vocabulary) and reads a sentence into tokens.</summary>
public static partial class GroundingCheck
{
    // ---------------------------------------------------------------- facts as the check sees them

    private enum FactKind { Title, AlsoKnownAs, Subject, Person, Character }

    private sealed record FactText(string Text, FactKind Kind, int Person);

    private sealed class Person(string name, bool isPrimary)
    {
        public string Name { get; } = name;
        public bool IsPrimary { get; set; } = isPrimary;
        public HashSet<string> Roles { get; } = [];
        public HashSet<string> NameTokens { get; } = [];
        public bool HasRole => Roles.Count > 0;
    }

    private sealed class FactModel
    {
        public required ExplanationFacts Facts { get; init; }
        public List<Person> Persons { get; } = [];
        public List<FactText> Texts { get; } = [];
        public HashSet<string> Vocabulary { get; } = new(StringComparer.Ordinal);
        public HashSet<string> TitleWords { get; } = new(StringComparer.Ordinal);
        public HashSet<string> MatchedWords { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Numbers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Roles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FactCharacters { get; } = new(StringComparer.Ordinal);
        public int MatchedContributor { get; private set; } = -1;

        public static FactModel From(ExplanationFacts f)
        {
            var m = new FactModel { Facts = f };
            foreach (var name in f.PrimaryAuthors) m.AddPerson(name, primary: true, role: null);
            foreach (var c in f.Contributors) m.AddPerson(c.Name, primary: false, role: c.Role);
            if (f.MatchedContributor is { } mc)
                m.MatchedContributor = m.AddPerson(mc, primary: false, role: f.Contributors.FirstOrDefault(c => ExplanationText.SameText(c.Name, mc))?.Role ?? RoleLexicon.Generic);
            if (f.AuthorAlias is { } alias && m.Persons.FindIndex(p => ExplanationText.SameText(p.Name, alias.Of)) is var of and >= 0)
            {
                m.Texts.Add(new FactText(alias.Alias, FactKind.Person, of));
                m.Persons[of].NameTokens.UnionWith(NameTokens(alias.Alias));
                m.AddWords(alias.Alias);
            }

            m.AddText(f.Title, FactKind.Title, title: true);
            if (f.AlsoKnownAs is { } aka)
            {
                // The label is used verbatim: the whole clause is one span, and its words ("known", "English") are no
                // vocabulary of their own, so "known in English as also published as …" fails.
                m.AddText($"{f.AlsoKnownAsLabel ?? "also published as"} {aka}", FactKind.AlsoKnownAs, title: true);
                m.AddText(aka, FactKind.AlsoKnownAs, title: true);
            }
            foreach (var ch in f.MatchedCharacters) m.AddText(ch, FactKind.Character, title: false);
            // Subject headings, like titles, count only when quoted whole: a crafted heading cannot lend its words to a new claim.
            foreach (var subject in f.MatchedSubjects) m.AddText(subject, FactKind.Subject, title: true);
            m.AddWords(f.MatchPhrase);
            foreach (var t in f.MatchedTerms)
            {
                m.MatchedWords.UnionWith(ExplanationText.Tokens(t));
                m.AddWords(t);
            }
            foreach (var c in f.Contributors)
            {
                m.Roles.Add(c.Role);
                m.AddWords(c.Role);
                m.AddWords(RoleLexicon.Plural(c.Role));
            }

            if (f.FirstPublishYear is { } y) m.Numbers.Add(y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            m.Numbers.Add(f.EditionCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (f.DuplicateRecords > 0) m.Numbers.Add(f.DuplicateRecords.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var word in m.Vocabulary.Where(w => w.All(char.IsAsciiDigit)).ToList()) m.Numbers.Add(TrimZeros(word));
            return m;
        }

        private int AddPerson(string name, bool primary, string? role)
        {
            var index = Persons.FindIndex(p => ExplanationText.SameText(p.Name, name));
            if (index < 0)
            {
                Persons.Add(new Person(name, primary));
                index = Persons.Count - 1;
                Persons[index].NameTokens.UnionWith(NameTokens(name));
                Texts.Add(new FactText(name, FactKind.Person, index));
                AddWords(name);
            }
            if (primary) Persons[index].IsPrimary = true;
            if (role is not null) Persons[index].Roles.Add(role);
            return index;
        }

        /// <summary>
        /// A title may be quoted whole (it is masked as one span), but its words are not free vocabulary: otherwise a
        /// crafted catalog title ("… free download at …") would supply the words for an injected claim. Typed title words
        /// come in through <see cref="ExplanationFacts.MatchedTerms"/>; numbers in a title stay allowed.
        /// </summary>
        private void AddText(string? text, FactKind kind, bool title)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Texts.Add(new FactText(text, kind, -1));
            if (!title)
            {
                AddWords(text);
                return;
            }
            var tokens = ExplanationText.Tokens(text);
            TitleWords.UnionWith(tokens);
            foreach (var digits in tokens.Where(t => t.All(char.IsAsciiDigit))) Numbers.Add(TrimZeros(digits));
            foreach (var rune in text.EnumerateRunes())
                if (!ExplanationText.IsWordRune(rune) && rune.Value != ' ') FactCharacters.Add(rune.ToString());
        }

        private void AddWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Vocabulary.UnionWith(ExplanationText.Tokens(text));
            foreach (var rune in text.EnumerateRunes())
                if (!ExplanationText.IsWordRune(rune) && rune.Value != ' ') FactCharacters.Add(rune.ToString());
        }

        private static IEnumerable<string> NameTokens(string name) =>
            ExplanationText.Tokens(name).Where(t => t.Length > 1 && !NameParticles.Contains(t));
    }

    // ---------------------------------------------------------------- tokens

    private enum TokenKind { Word, Number, Fact, Boundary, Period }

    private sealed record Token(TokenKind Kind, string Text, string Folded, bool Capitalized = false, int Fact = -1, bool UrlLike = false);

    private static class Tokenizer
    {
        public static List<Token> Read(string s, FactModel model, List<string> problems)
        {
            var owner = Mask(s, model);
            var tokens = new List<Token>();
            var i = 0;
            while (i < s.Length)
            {
                if (owner[i] >= 0)
                {
                    var span = owner[i];
                    var j = i;
                    while (j < s.Length && owner[j] == span) j++;
                    tokens.Add(new Token(TokenKind.Fact, s[i..j], "", Fact: span >> 8));
                    i = j;
                    continue;
                }

                var rune = Rune.GetRuneAt(s, i);
                if (ExplanationText.IsWordRune(rune))
                {
                    var j = ReadWord(s, i, owner);
                    var raw = s[i..j];
                    var folded = ExplanationText.Fold(raw);
                    var isNumber = folded.All(c => char.IsAsciiDigit(c) || c is ',' or '.') && folded.Any(char.IsAsciiDigit);
                    tokens.Add(new Token(isNumber ? TokenKind.Number : TokenKind.Word, raw, isNumber ? folded.Replace(",", "", StringComparison.Ordinal) : folded,
                        Capitalized: Rune.IsUpper(rune)));
                    i = j;
                    continue;
                }

                var c = s[i];
                switch (c)
                {
                    case '.':
                        var next = i + 1 < s.Length && ExplanationText.IsWordRune(Rune.GetRuneAt(s, i + 1));
                        var prevWord = tokens.Count > 0 && tokens[^1].Kind is TokenKind.Word or TokenKind.Number && tokens[^1].Folded.Length >= 2
                            && i > 0 && !char.IsWhiteSpace(s[i - 1]);
                        tokens.Add(new Token(TokenKind.Period, ".", ".", UrlLike: next && prevWord));
                        break;
                    case ',' or ';' or ':':
                        tokens.Add(new Token(TokenKind.Boundary, c.ToString(), c.ToString()));
                        break;
                    case '&':
                        tokens.Add(new Token(TokenKind.Boundary, "&", "and"));
                        break;
                    case '-' or '‐' or '‑' or '–' or '—':
                        tokens.Add(new Token(TokenKind.Boundary, c.ToString(), "-"));
                        break;
                    case '!' or '?':
                        problems.Add("not_a_statement");
                        break;
                }
                i += rune.Utf16SequenceLength;
            }
            return tokens;
        }

        /// <summary>Letters, marks and digits, joined across inner apostrophes/hyphens and digit groups ("3,196").</summary>
        private static int ReadWord(string s, int start, int[] owner)
        {
            var j = start;
            while (j < s.Length && owner[j] < 0)
            {
                var r = Rune.GetRuneAt(s, j);
                if (ExplanationText.IsWordRune(r)) { j += r.Utf16SequenceLength; continue; }
                var c = s[j];
                var nextIsWord = j + 1 < s.Length && owner[j + 1] < 0 && ExplanationText.IsWordRune(Rune.GetRuneAt(s, j + 1));
                if (c is '\'' or '’' or '-' or '‐' or '‑' && nextIsWord) { j++; continue; }
                if (c == ',' && j + 3 < s.Length && s[(j + 1)..(j + 4)].All(char.IsAsciiDigit) && (j + 4 == s.Length || !char.IsAsciiDigit(s[j + 4]))
                    && s[start..j].All(char.IsAsciiDigit)) { j += 4; continue; }
                if (c == '.' && j > start && char.IsAsciiDigit(s[j - 1]) && j + 1 < s.Length && char.IsAsciiDigit(s[j + 1])) { j++; continue; }
                break;
            }
            return j;
        }

        /// <summary>
        /// Marks every occurrence of a fact string (title, names, characters), ignoring case, accents, spacing and
        /// punctuation ("J. R. R. Tolkien" = "J.R.R. Tolkien"), at word boundaries, longest first. Each char gets a span id
        /// (fact index &lt;&lt; 8 | occurrence) or -1.
        /// </summary>
        private static int[] Mask(string s, FactModel model)
        {
            var owner = Enumerable.Repeat(-1, s.Length).ToArray();
            var (skeleton, source) = ExplanationText.Skeleton(s);
            var order = model.Texts
                .Select((t, index) => (Skeleton: ExplanationText.Skeleton(t.Text).Skeleton, index, t.Text))
                .Where(x => x.Skeleton.Length > 0)
                .OrderByDescending(x => x.Skeleton.Length)
                .ThenBy(x => x.index);
            foreach (var (fact, index, factText) in order)
            {
                var occurrence = 0;
                for (var at = skeleton.IndexOf(fact, StringComparison.Ordinal); at >= 0; at = skeleton.IndexOf(fact, at + 1, StringComparison.Ordinal))
                {
                    var start = source[at];
                    var last = source[at + fact.Length - 1];
                    var end = last + (char.IsHighSurrogate(s[last]) ? 2 : 1);
                    if (!AtWordBoundary(s, start, end) || owner.AsSpan(start, end - start).ContainsAnyExcept(-1)
                        || !OnlyFactSeparators(s.AsSpan(start, end - start), factText)) continue;
                    // Punctuation that is part of the fact ("H.D.", "Who's Afraid of Virginia Woolf?", "¡Viva!") belongs to the span.
                    var (prefix, suffix) = OuterPunctuation(factText);
                    if (suffix.Length > 0 && s.AsSpan(end).StartsWith(suffix, StringComparison.Ordinal) && owner[end] < 0) end += suffix.Length;
                    if (prefix.Length > 0 && start >= prefix.Length && s.AsSpan(0, start).EndsWith(prefix, StringComparison.Ordinal) && owner[start - prefix.Length] < 0) start -= prefix.Length;
                    var span = (index << 8) | (occurrence++ & 0xFF);
                    owner.AsSpan(start, end - start).Fill(span);
                }
            }
            return owner;
        }

        /// <summary>
        /// Between the fact's letters only spaces, initials' dots, hyphens, apostrophes and the fact's own punctuation may
        /// appear: "J. R. R. Tolkien" matches "J.R.R. Tolkien", but "Bears; free download" does not match a heading
        /// "Bears free download" (a clause break smuggled into a quoted fact).
        /// </summary>
        private static bool OnlyFactSeparators(ReadOnlySpan<char> span, string factText)
        {
            foreach (var c in span)
                if (!char.IsLetterOrDigit(c) && !char.IsSurrogate(c) && !char.IsWhiteSpace(c) && c is not ('.' or '-' or '\'' or '’')
                    && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !factText.Contains(c))
                    return false;
            return true;
        }

        private static (string Prefix, string Suffix) OuterPunctuation(string text)
        {
            text = text.Trim();
            var first = 0;
            while (first < text.Length && !char.IsLetterOrDigit(text[first]) && !char.IsSurrogate(text[first])) first++;
            var last = text.Length;
            while (last > first && !char.IsLetterOrDigit(text[last - 1]) && !char.IsSurrogate(text[last - 1])) last--;
            return (text[..first], text[last..]);
        }

        private static bool AtWordBoundary(string s, int start, int end)
        {
            if (start > 0)
            {
                var before = char.IsLowSurrogate(s[start - 1]) && start >= 2 ? Rune.GetRuneAt(s, start - 2) : new Rune(char.IsSurrogate(s[start - 1]) ? ' ' : s[start - 1]);
                if (ExplanationText.IsWordRune(before)) return false;
            }
            return end >= s.Length || !ExplanationText.IsWordRune(Rune.GetRuneAt(s, end));
        }
    }
}
