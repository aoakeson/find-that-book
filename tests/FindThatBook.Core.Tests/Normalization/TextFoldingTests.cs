using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Normalization;

public class TextFoldingTests
{
    [Theory]
    // Latin, Greek, Cyrillic: accents go, letters stay (DE-07, IN-023/024/032)
    [InlineData("diaeresis", "Brontë", "bronte")]
    [InlineData("acute", "Les Misérables", "les miserables")]
    [InlineData("IN-024 decomposed combining acute", "E\u0301mile Zola", "emile zola")]
    [InlineData("stroke letters and ligatures fold", "Æsop Œdipus Øre Straße Łódź Đorđe Þór ı", "aesop oedipus ore strasse lodz dorde thor i")]
    [InlineData("DE-07 Cyrillic keeps letters, й folds like Open Library", "Анна Каренина Толстой", "анна каренина толстои")]
    [InlineData("IN-032 Greek polytonic", "Ὀδύσσεια", "οδυσσεια")]
    [InlineData("Greek final sigma", "ΟΔΥΣΣΕΥΣ", "οδυσσευς")]
    [InlineData("final sigma before an apostrophe", "ΟΔΥΣΣΕΥΣ' ΣΑ", "οδυσσευς' σα")]
    [InlineData("Turkish dotted capital I", "İstanbul", "istanbul")]
    [InlineData("Vietnamese stacked marks", "Truyện Kiều", "truyen kieu")]
    // Scripts whose marks are spelling: unchanged except case (DE-07)
    [InlineData("DE-05 Han", "三体", "三体")]
    [InlineData("DE-07 Japanese with voiced kana", "ノルウェイの森 ドラゴンボール", "ノルウェイの森 ドラゴンボール")]
    [InlineData("Hangul stays syllables", "채식주의자", "채식주의자")]
    [InlineData("Devanagari keeps virama and vowel signs", "हिन्दी", "हिन्दी")]
    [InlineData("Thai keeps tone marks", "สี่แผ่นดิน", "สี่แผ่นดิน")]
    [InlineData("Thai SARA AM is not split by NFKD", "น้ำ", "น้ำ")]
    [InlineData("voiced kana keep their marks", "ドラゴンボール", "ドラゴンボール")]
    [InlineData("IN-033 Arabic hamza folds", "ألف ليلة وليلة", "الف ليلة وليلة")]
    // Compatibility forms and invisible characters (IN-018, IN-019)
    [InlineData("IN-018 full-width", "ｔｈｅ ｈｏｂｂｉｔ", "the hobbit")]
    [InlineData("IN-018 ligature", "ﬁnal", "final")]
    [InlineData("half-width katakana recompose voicing", "ﾄﾞﾗｺﾞﾝ", "ドラゴン")]
    [InlineData("IN-019 zero-width space deleted, not spaced", "hob\u200Bbit", "hobbit")]
    [InlineData("soft hyphen and bidi marks deleted", "hob\u00ADbit\u200F", "hobbit")]
    [InlineData("trademark sign is not glued on as letters", "Hobbit™", "hobbit™")]
    public void Fold_StripsAccentsButKeepsEveryScriptsLetters(string @case, string input, string expected)
    {
        _ = @case;
        TextFolding.Fold(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Anna Karenina, Cyrillic", "Анна Каренина")]
    [InlineData("Hangul", "채식주의자")]
    [InlineData("Japanese", "ノルウェイの森")]
    [InlineData("half-width kana", "ﾄﾞﾗｺﾞﾝ")]
    [InlineData("Devanagari", "हिन्दी")]
    [InlineData("Latin accents", "Brontë Æ ß ł")]
    public void Fold_IsIdempotent(string @case, string input)
    {
        _ = @case;
        var once = TextFolding.Fold(input);
        TextFolding.Fold(once).ShouldBe(once);
    }

    [Fact]
    public void Fold_NeverSendsHangulAsJamo()
    {
        var folded = TextFolding.Fold("채식주의자 한강");
        folded.ShouldNotContain(c => c >= '\u1100' && c <= '\u11FF');
        folded.ShouldBe("채식주의자 한강");
        // decomposed (conjoining jamo) input is recomposed into syllables
        TextFolding.Fold("\u1112\u1161\u11AB\u1100\u1161\u11BC").ShouldBe("한강");
    }

    [Theory]
    [InlineData("curly apostrophe joins", "harry potter philosopher’s stone", "harry potter philosophers stone")]
    [InlineData("trailing possessive", "dickens' great expectations", "dickens great expectations")]
    [InlineData("acute accent used as apostrophe", "philosopher´s stone", "philosophers stone")]
    [InlineData("ampersand", "pride & prejudice", "pride and prejudice")]
    [InlineData("digit groups", "20,000 leagues", "20000 leagues")]
    [InlineData("single letters stay", "j r r tolkien hobbit", "j r r tolkien hobbit")]
    [InlineData("IN-028 French elision dropped", "l'étranger camus", "etranger camus")]
    [InlineData("IN-028 curly elision", "L’Étranger", "etranger")]
    [InlineData("Italian elision", "dell'amore", "amore")]
    [InlineData("contraction is not an elision", "don't look now", "dont look now")]
    [InlineData("O' names join", "o'brien", "obrien")]
    [InlineData("IN-022 punctuation title", "who's afraid of virginia woolf?", "whos afraid of virginia woolf")]
    [InlineData("IN-085 plus is a separator", "romeo + juliet", "romeo juliet")]
    [InlineData("IN-020 emoji dropped", "harry potter 📚", "harry potter")]
    [InlineData("IN-017 no-break space", "the\u00A0hobbit", "the hobbit")]
    [InlineData("underscore splits", "where_the_sidewalk", "where the sidewalk")]
    [InlineData("Indic word is one token", "हिन्दी उपन्यास", "हिन्दी उपन्यास")]
    [InlineData("Devanagari word stays whole", "गोदान premchand", "गोदान premchand")]
    public void Tokens_FollowTheMeasuredRules(string @case, string input, string expected)
    {
        _ = @case;
        string.Join(' ', TextFolding.Tokens(input)).ShouldBe(expected);
    }

    [Fact]
    public void Content_DropsStopWordsOnly()
    {
        TextFolding.Content(["the", "lord", "of", "the", "rings", "book"]).ShouldBe(["lord", "rings", "book"]);
    }

    [Theory]
    [InlineData("三体", 2)]
    [InlineData("it", 2)]
    [InlineData("𠀀𠀁", 2)] // astral CJK: 4 UTF-16 units, 2 code points
    [InlineData("v", 1)]
    public void CodePoints_CountsLikeOpenLibrary(string text, int expected) => TextFolding.CodePoints(text).ShouldBe(expected);

    [Theory]
    [InlineData("L'Étranger", true)]
    [InlineData("Cien años de soledad", true)]
    [InlineData("Детство", false)]
    [InlineData("容疑者Xの献身", false)]
    [InlineData("1984", false)]
    public void IsLatinScript(string text, bool expected) => TextFolding.IsLatinScript(text).ShouldBe(expected);

    [Fact]
    public void UnfoldedTokens_KeepAccentsAndScript()
    {
        TextFolding.UnfoldedTokens("L'Étranger").ShouldBe(["étranger"]);
        TextFolding.UnfoldedTokens("Ὀδύσσεια").ShouldBe(["ὀδύσσεια"]);
        TextFolding.UnfoldedTokens("채식주의자").ShouldBe(["채식주의자"]);
        TextFolding.UnfoldedTokens("Les Mise\u0301rables").ShouldBe(["les", "misérables"]); // NFC composes
    }
}
