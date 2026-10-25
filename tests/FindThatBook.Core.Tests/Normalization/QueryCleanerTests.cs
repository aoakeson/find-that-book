using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Normalization;

public class QueryCleanerTests
{
    [Theory]
    // search tokens and extracted year
    [InlineData("noise and year out", "Tolkien Hobbit Illustrated Deluxe 1937", "tolkien hobbit", 1937)]
    [InlineData("ampersand optional", "pride & prejudice", "pride prejudice", null)]
    [InlineData("year kept with < 2 other words", "1984 orwell", "1984 orwell", null)]
    [InlineData("bare year is the title", "1984", "1984", null)]
    [InlineData("publisher noise and year", "great gatsby fitzgerald penguin classics 2003", "great gatsby fitzgerald", 2003)]
    [InlineData("curly apostrophe", "harry potter philosopher’s stone", "harry potter philosophers stone", null)]
    [InlineData("trailing apostrophe", "dickens' great expectations", "dickens great expectations", null)]
    [InlineData("accent", "Brontë jane eyre", "bronte jane eyre", null)]
    [InlineData("brackets and stop words", "The Adventures of Huckleberry Finn by Mark Twain (Illustrated) [Annotated]", "adventures huckleberry finn mark twain", null)]
    [InlineData("bare or", "the hobbit or there and back again", "hobbit there back again", null)]
    [InlineData("all stop words fallback", "the it", "it", null)]
    [InlineData("1st kindle noise", "buchan mr standfast 1st kindle 1918", "buchan mr standfast", 1918)]
    // the broad search sends the digit form; a lone "one" stays a word
    [InlineData("number words to digits", "nineteen eighty four orwell", "1984 orwell", null)]
    [InlineData("lone one stays a word", "the one with pigs", "one pigs", null)]
    [InlineData("thousand", "twenty thousand leagues under the sea", "20000 leagues under sea", null)]
    [InlineData("IN-048 catch-22", "catch-22 heller", "catch 22 heller", null)]
    // pasted markup
    [InlineData("IN-026 HTML entity", "pride &amp; prejudice", "pride prejudice", null)]
    [InlineData("HTML apostrophe entity", "harry potter philosopher&#39;s stone", "harry potter philosophers stone", null)]
    [InlineData("markdown link keeps its text", "[The Hobbit](https://openlibrary.org/works/OL27482W)", "hobbit", null)]
    [InlineData("Amazon URL slug", "https://www.amazon.com/Left-Hand-Darkness-Ursula-Guin/dp/0441478123/ref=sr_1_1", "left hand darkness ursula guin", null)]
    [InlineData("Goodreads URL slug", "https://www.goodreads.com/book/show/5907.The_Hobbit", "hobbit", null)]
    [InlineData("IN-075 Open Library URL slug", "https://openlibrary.org/works/OL27482W/The_Hobbit", "hobbit", null)]
    [InlineData("IN-027 leftover percent escapes", "pride%20and%20prejudice", "pride prejudice", null)]
    // QA rows fixed in the cleaner
    [InlineData("IN-011 repeated words", "hobbit hobbit hobbit", "hobbit", null)]
    [InlineData("IN-018 full-width", "ｔｈｅ ｈｏｂｂｉｔ", "hobbit", null)]
    [InlineData("IN-019 zero-width inside a word", "hob\u200Bbit", "hobbit", null)]
    [InlineData("IN-015 control character", "harry potter\u0000", "harry potter", null)]
    [InlineData("IN-028 elision", "l'étranger camus", "etranger camus", null)]
    [InlineData("IN-029 elision typed apart", "l etranger camus", "etranger camus", null)]
    [InlineData("initial before a consonant stays", "j d salinger", "j d salinger", null)]
    [InlineData("IN-065 name suffix", "kurt vonnegut jr. cat's cradle", "kurt vonnegut cats cradle", null)]
    [InlineData("IN-057 number after a removed series word", "hunger games book 2", "hunger games", null)]
    [InlineData("IN-057 number word after book", "hunger games book two", "hunger games", null)]
    [InlineData("IN-057 hash number", "dune #1", "dune", null)]
    [InlineData("vol numbers stay (titles carry them)", "naruto vol 2", "naruto vol 2", null)]
    [InlineData("a number after 'novel' stays", "agatha christie novel ten people island", "agatha christie 10 people island", null)]
    [InlineData("IN-011 dedupe after numerals", "twenty-six sixty-six bolano", "2666 bolano", null)]
    [InlineData("decoded escapes are sanitized again", "passwd%00 hobbit&#8203;s", "passwd hobbits", null)]
    [InlineData("IN-047 year-shaped title prefix", "2001 a space odyssey", "space odyssey", 2001)]
    [InlineData("IN-056 noise-only query is sent as typed", "the collector", "the collector", null)]
    [InlineData("DE-05 Han", "三体", "三体", null)]
    [InlineData("DE-07 Cyrillic", "Анна Каренина", "анна каренина", null)]
    [InlineData("Hangul", "채식주의자", "채식주의자", null)]
    public void Clean_SearchTokensAndYear(string @case, string raw, string search, int? year)
    {
        _ = @case;
        var q = QueryCleaner.Clean(raw);
        string.Join(' ', q.Search).ShouldBe(search);
        q.Year.ShouldBe(year);
    }

    [Fact]
    public void Clean_IN012_DropsJunkTokensButKeepsTheWords()
    {
        var q = QueryCleaner.Clean("tolkien hobbit " + new string('x', 285));
        q.Search.ShouldBe(["tolkien", "hobbit"]);
        q.Optional.ShouldContain(new string('x', 285));
    }

    [Theory]
    [InlineData("IN-044 one title word + year: soft, sent", "the hobbit 1937", "hobbit 1937", "1937")]
    [InlineData("author + year: soft", "1984 orwell", "1984 orwell", "1984")]
    [InlineData("the year is the whole title: not soft", "1984", "1984", "")]
    [InlineData("IN-045 extracted year is not soft", "1776 david mccullough", "david mccullough", "")]
    [InlineData("no year", "the hobbit", "hobbit", "")]
    public void Clean_SoftYearTokens(string @case, string raw, string search, string soft)
    {
        _ = @case;
        var q = QueryCleaner.Clean(raw);
        string.Join(' ', q.Search).ShouldBe(search);
        string.Join(' ', q.SoftTokens.Order()).ShouldBe(soft);
        if (soft.Length > 0) q.Year.ShouldBeNull("a kept year is not also extracted");
    }

    [Fact]
    public void Clean_OptionalHoldsNoiseAndYear_RemovedNoiseListsFormatWords()
    {
        var q = QueryCleaner.Clean("Tolkien Hobbit Illustrated Deluxe 1937");
        q.Optional.Order().ShouldBe(["1937", "deluxe", "illustrated"]);
        q.RemovedNoise.ShouldBe(["illustrated", "deluxe"]);
        q.All.ShouldBe(["tolkien", "hobbit", "illustrated", "deluxe", "1937"]);
        q.Intents.ShouldBeEmpty("edition words are not intents");
    }

    [Theory]
    [InlineData("head before bare or", "the hobbit or there and back again", "hobbit")]
    [InlineData("leading or gives no head", "or the hobbit or there", "")]
    [InlineData("no or", "the hobbit", "")]
    public void Clean_Head(string @case, string raw, string head)
    {
        _ = @case;
        string.Join(' ', QueryCleaner.Clean(raw).Head).ShouldBe(head);
    }

    [Theory]
    // adaptation and study intents only; omnibus is off
    [InlineData("graphic novel", "hobbit graphic novel", new[] { EditionIntent.Adaptation })]
    [InlineData("sparknotes", "sparknotes catcher in the rye", new[] { EditionIntent.StudyGuide })]
    [InlineData("edition words are not intents", "tolkien hobbit illustrated deluxe 1937", new EditionIntent[0])]
    [InlineData("box set: omnibus off", "harry potter box set", new EditionIntent[0])]
    [InlineData("study guide pair", "great gatsby study guide", new[] { EditionIntent.StudyGuide })]
    [InlineData("cliffs notes typed apart", "cliffs notes hamlet", new[] { EditionIntent.StudyGuide })]
    [InlineData("manga", "death note manga", new[] { EditionIntent.Adaptation })]
    [InlineData("study alone needs its pair", "a study in scarlet", new EditionIntent[0])]
    public void Clean_Intents(string @case, string raw, EditionIntent[] expected)
    {
        _ = @case;
        QueryCleaner.Clean(raw).Intents.Order().ShouldBe(expected.Order());
    }

    [Fact]
    public void Clean_KeepsRawAndCleanedText()
    {
        var q = QueryCleaner.Clean("  pride &amp; prejudice\u200B ");
        q.Raw.ShouldBe("pride &amp; prejudice");
        q.Text.ShouldBe("pride & prejudice");
    }

    [Theory]
    [InlineData("DE-05 stop words only", "the")]
    [InlineData("punctuation only", "?!? ... ###")]
    public void Clean_NothingSearchable_IsEmptyOnlyWhenNoTokensAtAll(string @case, string raw)
    {
        _ = @case;
        var q = QueryCleaner.Clean(raw);
        // "the" keeps the stop-word fallback (the API rejects it first: no_searchable_terms); punctuation has no tokens.
        q.IsEmpty.ShouldBe(q.All.Count == 0);
    }
}
