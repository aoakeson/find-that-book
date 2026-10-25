using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Core.Normalization;

/// <summary>Folding and tokenizing, with the DE-07 script rule.</summary>
public static partial class TextFolding
{
    /// <summary>Letters with no Unicode decomposition that still fold to their Latin base.</summary>
    private static readonly FrozenDictionary<char, string> LetterFolds = new Dictionary<char, string>
    {
        ['æ'] = "ae", ['œ'] = "oe", ['ø'] = "o", ['ß'] = "ss", ['ł'] = "l", ['đ'] = "d", ['ð'] = "d", ['þ'] = "th", ['ı'] = "i",
    }.ToFrozenDictionary();

    /// <summary>
    /// Lowercase and strip accents without dropping letters in any script (DE-07). Plain NFKD-and-drop-every-combining-
    /// mark is right for Latin/Greek/Cyrillic but wrong elsewhere: it leaves Hangul as jamo, kana without voicing
    /// (ガ → カ), Devanagari without virama/nukta and Thai without tone marks. Rule here:
    /// <list type="number">
    /// <item>Format characters (zero-width, bidi, soft hyphen, BOM: Cf) and variation selectors are deleted (IN-019).</item>
    /// <item>NFKD, so compatibility forms fold (full-width ｈｏｂｂｉｔ, ligature ﬁ, superscripts: IN-018). Symbols
    /// (™ → "TM" would glue onto the word) and Hangul compatibility jamo stay as typed.</item>
    /// <item>Combining marks are dropped only when they sit on a Latin, Greek or Cyrillic letter (accents; Open Library
    /// folds these too: probes 10/24 give identical hits for война/воина, and Ὀδύσσεια is found folded, IN-032) or on
    /// a Hebrew/Arabic/Syriac letter (optional vowel points; IN-033), and when they have no letter to sit on. Marks of
    /// every other script are spelling and stay.</item>
    /// <item>NFC, so the scripts that kept their marks are sent upstream exactly as typed: Hangul syllables (never jamo:
    /// the index holds syllables), voiced kana, Indic vowel signs.</item>
    /// <item>Lowercase (Greek final sigma per Unicode), then æ→ae, œ→oe, ø→o, ß→ss, ł→l, đ/ð→d, þ→th, ı→i.</item>
    /// </list>
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var kept = new StringBuilder(text.Length + 8);
        var marksStay = false; // the last base letter belongs to a script whose marks are spelling
        foreach (var rune in Decompose(text).EnumerateRunes())
        {
            if (IsDeleted(rune)) continue;
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
            {
                if (marksStay) kept.Append(rune.ToString());
                continue;
            }
            kept.Append(rune.ToString());
            if (cat == UnicodeCategory.SpacingCombiningMark) continue; // an Indic vowel sign keeps its consonant's script
            marksStay = Rune.IsLetter(rune) && !HasOptionalMarks(rune.Value);
        }
        return Lower(kept.ToString().Normalize(NormalizationForm.FormC), foldLetters: true);
    }

    /// <summary>Fold, join apostrophes (elided articles dropped, IN-028), &amp; → and, digit groups (20,000), split on non letters/marks/digits.</summary>
    public static IReadOnlyList<string> Tokens(string? text) => Split(PrepareWords(Fold(text)));

    /// <summary>Tokens minus stop words.</summary>
    public static IReadOnlyList<string> Content(IReadOnlyList<string> tokens) => [.. tokens.Where(t => !Vocabulary.Stop.Contains(t))];

    /// <summary>
    /// Word tokens <b>without</b> folding: NFC, lowercased, format characters deleted, apostrophes handled as in
    /// <see cref="Tokens"/>, accents kept. For text sent to Open Library in the LLM's own words (original titles).
    /// </summary>
    public static IReadOnlyList<string> UnfoldedTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            if (!IsDeleted(rune)) kept.Append(rune.ToString());
        return Split(PrepareWords(Lower(kept.ToString().Normalize(NormalizationForm.FormC), foldLetters: false)));
    }

    /// <summary>A letter, combining mark or number: what a token is made of (marks included, so Indic and Thai words stay whole).</summary>
    public static bool IsWordChar(Rune r) => Rune.GetUnicodeCategory(r) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
        _ => false,
    };

    /// <summary>True when the text has letters and every one is Latin script (accents allowed).</summary>
    public static bool IsLatinScript(string text)
    {
        var any = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(r)) continue;
            if (!IsLatin(r.Value)) return false;
            any = true;
        }
        return any;
    }

    /// <summary>All digits (any script).</summary>
    public static bool IsDigits(string token) => token.Length > 0 && token.All(char.IsDigit);

    /// <summary>Text length the way Open Library counts it (code points), for the 3-character q= minimum (DE-05).</summary>
    public static int CodePoints(string text) => text.EnumerateRunes().Count();

    // ---- folding internals ----

    /// <summary>
    /// NFKD, leaving as typed: symbols, Hangul compatibility jamo, and the few letters of kept-mark scripts whose
    /// compatibility decomposition NFC does not recompose (Thai น้ำ must not become น้ํา). ´ is treated as an apostrophe.
    /// </summary>
    private static string Decompose(string text)
    {
        var output = new StringBuilder(text.Length + 8);
        var chunk = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (!KeepsAsTyped(rune))
            {
                chunk.Append(rune.ToString());
                continue;
            }
            output.Append(chunk.ToString().Normalize(NormalizationForm.FormKD));
            chunk.Clear();
            output.Append(rune.Value == '´' ? "'" : rune.ToString());
        }
        return output.Append(chunk.ToString().Normalize(NormalizationForm.FormKD)).ToString();
    }

    private static bool KeepsAsTyped(Rune r) =>
        r.Value is >= 0x3131 and <= 0x318E or >= 0xFFA0 and <= 0xFFDC // NFKD would turn these into conjoining jamo
            or 0x0E33 or 0x0EB3 or 0x0EDC or 0x0EDD or 0x0F77 or 0x0F79 // Thai/Lao SARA AM etc.: NFC can't undo their NFKD
        || Rune.GetUnicodeCategory(r) is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

    private static bool IsDeleted(Rune r) =>
        r.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF or 0x034F // variation selectors, grapheme joiner
        || Rune.GetUnicodeCategory(r) == UnicodeCategory.Format;

    /// <summary>Scripts whose combining marks are optional (accents, vowel points): Latin, Greek, Cyrillic, Hebrew, Arabic, Syriac.</summary>
    private static bool HasOptionalMarks(int v) => IsLatin(v)
        || v is >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF                                   // Greek
        || v is >= 0x0400 and <= 0x052F or >= 0x1C80 and <= 0x1C8F or >= 0x2DE0 and <= 0x2DFF or >= 0xA640 and <= 0xA69F // Cyrillic
        || v is >= 0x0590 and <= 0x077F or >= 0x0860 and <= 0x08FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF; // Hebrew, Arabic, Syriac (not Thaana 0780–07BF: its vowels are spelling)

    private static bool IsLatin(int v) => v is < 0x0300 or >= 0x1D00 and <= 0x1DBF or >= 0x1E00 and <= 0x1EFF
        or >= 0x2C60 and <= 0x2C7F or >= 0xA720 and <= 0xA7FF or >= 0xAB30 and <= 0xAB6F or >= 0xFF21 and <= 0xFF5A;

    private static string Lower(string s, bool foldLetters)
    {
        var lowered = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == 'Σ')
            {
                lowered.Append(IsFinalSigma(s, i) ? 'ς' : 'σ');
                continue;
            }
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                lowered.Append(Rune.ToLowerInvariant(new Rune(c, s[i + 1])).ToString());
                i++;
                continue;
            }
            var lower = char.ToLowerInvariant(c);
            if (foldLetters && LetterFolds.TryGetValue(lower, out var folded)) lowered.Append(folded);
            else lowered.Append(lower);
        }
        return lowered.ToString();
    }

    /// <summary>Unicode's Final_Sigma context: a cased letter before, none after, skipping case-ignorables.</summary>
    private static bool IsFinalSigma(string s, int at)
    {
        var before = at - 1;
        while (before >= 0 && IsCaseIgnorable(s[before])) before--;
        if (before < 0 || !IsCased(s[before])) return false;
        var after = at + 1;
        while (after < s.Length && IsCaseIgnorable(s[after])) after++;
        return after >= s.Length || !IsCased(s[after]);
    }

    private static bool IsCased(char c) => char.IsUpper(c) || char.IsLower(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter;

    private static bool IsCaseIgnorable(char c) => c is '\'' or '’' or '.' or ':' or '·' || CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.ModifierLetter
        or UnicodeCategory.ModifierSymbol;

    // ---- tokenizing internals ----

    private static string PrepareWords(string s)
    {
        s = Elision().Replace(s, "");    // l'étranger → étranger, dell'amore → amore (IN-028)
        s = Apostrophe().Replace(s, ""); // philosopher's → philosophers ; dickens' → dickens
        s = s.Replace("&", " and ");
        return DigitGroup().Replace(s, ""); // 20,000 → 20000
    }

    private static List<string> Split(string s)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var r in s.EnumerateRunes())
        {
            if (IsWordChar(r))
            {
                current.Append(r.ToString());
                continue;
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            current.Clear();
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// French/Italian elided articles and "de"/"que" at the start of a word, before a letter (IN-028), dropped as
    /// Lucene's ElisionFilter does, so "l'étranger" and the catalog's "L’étranger" both give "etranger" (and
    /// QueryCleaner makes a typed-apart "l etranger" agree). English is unaffected: possessives and contractions put the
    /// apostrophe after the word ("dickens'", "don't"). The pronoun elisions m' t' s' n' c' are left out on purpose:
    /// they collide with names (M'Coul, T'Challa) for little title value. Live 10/24: q=l etranger camus and
    /// q=letranger camus both rank OL1230613W #1.
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\p{M}\p{N}])(?:l|d|j|qu|dell|dall|nell|sull|all|quell)['’ʼ`´‘](?=\p{L})")]
    private static partial Regex Elision();

    /// <summary>Apostrophes ' ’ ʼ ` ´ ‘ are deleted: "philosopher's" → "philosophers" (Open Library stems both).</summary>
    [GeneratedRegex(@"['’ʼ`´‘]")]
    private static partial Regex Apostrophe();

    [GeneratedRegex(@"(?<=\d),(?=\d{3}\b)")]
    private static partial Regex DigitGroup();
}
