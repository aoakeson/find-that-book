using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Normalization;

public class TokenMatchTests
{
    [Theory]
    // typos
    [InlineData("transposition", "tolkein", "tolkien", true)]
    [InlineData("deletion", "dikens", "dickens", true)]
    [InlineData("two edits on a long word", "expecttions", "expectations", true)]
    [InlineData("different first letter", "higgins", "dwiggins", false)]
    [InlineData("numbers never fuzzy", "1984", "1985", false)]
    [InlineData("short words must be equal", "cat", "car", false)]
    // prefix guard and its plural exception
    [InlineData("IN-037 prefix pair blocked", "prince", "princess", false)]
    [InlineData("prefix pair blocked", "king", "kingdom", false)]
    [InlineData("plural", "ghost", "ghosts", true)]
    [InlineData("doubled last letter", "giver", "giverr", true)]
    [InlineData("plural of a 4-letter word", "king", "kings", true)]
    [InlineData("DA-052 real-word collision, one edit", "night", "right", true)]
    [InlineData("DA-052 real-word collision woman/women", "woman", "women", true)]
    [InlineData("two edits on 7 letters is not equal", "hobbits", "rabbits", false)]
    public void Equal(string @case, string a, string b, bool expected)
    {
        _ = @case;
        TokenMatch.Equal(a, b).ShouldBe(expected);
        TokenMatch.Equal(b, a).ShouldBe(expected);
    }

    [Theory]
    [InlineData("tolkein", "tolkien", 1)]
    [InlineData("expecttions", "expectations", 1)]
    [InlineData("hobbit", "rabbit", 2)]
    [InlineData("a", "abcdef", 4)] // length gap above the cap
    public void Distance(string a, string b, int expected) => TokenMatch.Distance(a, b).ShouldBe(expected);

    [Theory]
    // grounding only, never ranking
    [InlineData("one edit on 3 letters", "tee", "tea", true)]
    [InlineData("two edits, same first letter, 5+ letters", "jumbys", "jumbies", true)]
    [InlineData("prefix pairs never support", "prince", "princess", false)]
    [InlineData("numbers never fuzzy", "12", "13", false)]
    [InlineData("TokEq implies support", "tolkein", "tolkien", true)]
    [InlineData("two edits, different first letter", "jumbys", "gumbies", false)]
    public void Supports(string @case, string typed, string titleWord, bool expected)
    {
        _ = @case;
        TokenMatch.Supports(typed, titleWord).ShouldBe(expected);
    }

    [Theory]
    // literal partners first; exact needs every pair literal (IN-011)
    [InlineData("partial title", "huckleberry", "adventures huckleberry finn", false, 0.6667, 1.0, 0.3333)]
    [InlineData("query number words vs title digits (TitleText.Sets)", "tale two cities", "tale 2 cities", true, 1.0, 1.0, 1.0)]
    [InlineData("a plural pair matches but is not exact", "little princes", "little prince", false, 1.0, 1.0, 1.0)]
    [InlineData("one fuzzy pair breaks exact", "hobit", "hobbit", false, 1.0, 1.0, 1.0)]
    [InlineData("query number words become digits", "nineteen eighty four", "1984", true, 1.0, 1.0, 1.0)]
    [InlineData("IN-011 repeated words count once", "hobbit hobbit hobbit", "hobbit", true, 1.0, 1.0, 1.0)]
    [InlineData("literal partner wins over a fuzzy one", "women love", "women love", true, 1.0, 1.0, 1.0)]
    public void Score(string @case, string query, string title, bool exact, double score, double precision, double recall)
    {
        _ = @case;
        var m = TokenMatch.Score(query.Split(' '), title.Split(' '));
        m.Exact.ShouldBe(exact);
        m.Score.ShouldBe(score, 0.001);
        m.Precision.ShouldBe(precision, 0.001);
        m.Recall.ShouldBe(recall, 0.001);
    }

    [Fact]
    public void Score_EmptySideIsNoMatch()
    {
        TokenMatch.Score([], ["hobbit"]).ShouldBe(TitleMatch.NoMatch);
        TokenMatch.Score(["hobbit"], []).ShouldBe(TitleMatch.NoMatch);
    }

    [Theory]
    // a typed title complete within one edit per word
    [InlineData("one edit per word", "murdr orient express", "murder orient express", true)]
    [InlineData("dogfish", "dogfsh", "dogfish", true)]
    [InlineData("count must match", "murder orient", "murder orient express", false)]
    [InlineData("short words need a literal partner", "cat hat", "cat bat", false)]
    [InlineData("two edits is too far", "mrdr", "murder", false)]
    [InlineData("prefix pair is not a typo", "little princess", "little prince", false)]
    [InlineData("plural counts", "ghosts", "ghost", true)]
    public void FuzzyComplete(string @case, string query, string title, bool expected)
    {
        _ = @case;
        TokenMatch.FuzzyComplete(query.Split(' '), title.Split(' ')).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ghost", "ghosts", true)]
    [InlineData("giverr", "giver", true)]
    [InlineData("prince", "princess", false)]
    [InlineData("king", "kingdom", false)]
    public void PrefixVariant(string a, string b, bool expected) => TokenMatch.PrefixVariant(a, b).ShouldBe(expected);
}

public class NumeralsTests
{
    [Theory]
    [InlineData("year style", "nineteen eighty four", "1984")]
    [InlineData("year style 1812", "eighteen twelve", "1812")]
    [InlineData("tens + unit", "thirty nine steps", "39 steps")]
    [InlineData("hundreds", "fahrenheit four hundred fifty one", "fahrenheit 451")]
    [InlineData("thousand", "twenty thousand leagues", "20000 leagues")]
    [InlineData("and inside a number", "one hundred and one dalmatians", "101 dalmatians")]
    [InlineData("zero", "zero", "0")]
    [InlineData("no number words: unchanged", "the hobbit", "the hobbit")]
    [InlineData("chunked non-year: only the last word converts", "four five six", "four five 6")]
    public void ToDigits(string @case, string input, string expected)
    {
        _ = @case;
        string.Join(' ', Numerals.ToDigits(input.Split(' '))).ShouldBe(expected);
    }

    [Theory]
    [InlineData("lone one stays", "one with pigs", "one with pigs")]
    [InlineData("one inside a run converts", "twenty one", "21")]
    [InlineData("one next to a number word converts", "one hundred years solitude", "100 years solitude")]
    [InlineData("other words convert", "seven habits", "7 habits")]
    public void ToDigitsForSearch(string @case, string input, string expected)
    {
        _ = @case;
        string.Join(' ', Numerals.ToDigitsForSearch(input.Split(' '))).ShouldBe(expected);
    }

    [Theory]
    [InlineData("nineteen", 19)]
    [InlineData("eighty", 80)]
    [InlineData("hundred", null)]
    [InlineData("hobbit", null)]
    public void WordValue(string token, int? expected) => Numerals.WordValue(token).ShouldBe(expected);
}

public class VocabularyTests
{
    [Theory]
    // installment requests
    [InlineData("ordinal word", "the second hunger games", true)]
    [InlineData("3rd", "harry potter 3rd", true)]
    [InlineData("hash number", "dune #2", true)]
    [InlineData("glued series number", "wheel of time bk5", true)]
    [InlineData("number next to a series word", "hunger games book 2", true)]
    [InlineData("number word next to a series word", "narnia part two", true)]
    [InlineData("first alone is not an installment", "first love", false)]
    [InlineData("first next to a series word is", "first book of narnia", true)]
    [InlineData("plain number is not", "catch 22", false)]
    [InlineData("big number next to a series word is not", "book 451", false)]
    public void HasOrdinal(string @case, string raw, bool expected)
    {
        _ = @case;
        Vocabulary.HasOrdinal(QueryCleaner.Clean(raw)).ShouldBe(expected);
    }

    [Fact]
    public void WordLists_MatchTheMeasuredLists()
    {
        Vocabulary.Stop.Count.ShouldBe(16);
        Vocabulary.Noise.ShouldContain("illustrated");
        Vocabulary.Noise.ShouldNotContain("graphic");
        Vocabulary.AllIntentWords.ShouldContain("notes");
        Vocabulary.SeriesWords.ShouldContain("vol");
        Vocabulary.Ordinals.ShouldContain("10th");
    }
}
