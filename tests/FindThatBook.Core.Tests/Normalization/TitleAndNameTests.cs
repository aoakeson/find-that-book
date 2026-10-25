using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Normalization;

public class TitleTextTests
{
    [Theory]
    // title keys: subtitles split off, groups removed, number words as digits
    [InlineData("'. Or,' split", "The Hobbit. Or, There and Back Again", "hobbit", "hobbit there back again")]
    [InlineData("colon split", "The Hobbit: or There and Back Again", "hobbit", "hobbit there back again")]
    [InlineData("no bare-or split", "The Hobbit or There and Back Again", "hobbit there back again", "hobbit there back again")]
    [InlineData("[adaptation] removed, number words as digits", "A Tale of Two Cities [adaptation]", "tale 2 cities", "tale 2 cities")]
    [InlineData("(series, #n) removed", "Morgenrood (Twilight, #4)", "morgenrood", "morgenrood")]
    [InlineData("'; or,' split", "Frankenstein; or, The Modern Prometheus", "frankenstein", "frankenstein modern prometheus")]
    [InlineData("hyphenated main title", "Moby-Dick; or, The Whale", "moby dick", "moby dick whale")]
    [InlineData("possessive joined", "Harry Potter and the Philosopher's Stone", "harry potter philosophers stone", "harry potter philosophers stone")]
    [InlineData("accents", "Les Misérables", "les miserables", "les miserables")]
    [InlineData("spaced dash split", "Great Expectations - Classic Book", "great expectations", "great expectations classic book")]
    [InlineData("Nineteen Eighty-Four", "Nineteen Eighty-Four", "1984", "1984")]
    [InlineData("Thirty-Nine Steps", "The Thirty-Nine Steps", "39 steps", "39 steps")]
    [InlineData("Slaughterhouse-Five", "Slaughterhouse-Five", "slaughterhouse 5", "slaughterhouse 5")]
    [InlineData("Seventeen Seventy-Six", "Seventeen Seventy-Six", "1776", "1776")]
    [InlineData("stop-word-only title keeps its words", "The", "the", "the")]
    [InlineData("IN-069 colon at start is not a split", ": Dune", "dune", "dune")]
    [InlineData("IN-028 elision in a catalog title", "L’étranger", "etranger", "etranger")]
    [InlineData("full-width colon splits", "涼宮ハルヒの憂鬱：上", "涼宮ハルヒの憂鬱", "涼宮ハルヒの憂鬱 上")]
    public void KeyAndSets(string @case, string title, string key, string full)
    {
        _ = @case;
        TitleText.Key(title).ShouldBe(key);
        var sets = TitleText.Sets(title);
        string.Join(' ', sets.Main).ShouldBe(key);
        string.Join(' ', sets.Full).ShouldBe(full);
    }

    [Fact]
    public void Key_ArticleDoesNotChangeTheKey()
    {
        TitleText.Key("The Adventures of Huckleberry Finn").ShouldBe(TitleText.Key("Adventures of Huckleberry Finn"));
    }

    [Fact]
    public void Sets_AddsTheSubtitleField()
    {
        var sets = TitleText.Sets("The Hobbit", "There and Back Again");
        sets.Main.ShouldBe(["hobbit"]);
        sets.Full.ShouldBe(["hobbit", "there", "back", "again"]);
    }

    [Fact]
    public void Split_ReturnsFoldedMainAndSubtitle()
    {
        TitleText.Split("Dune: Messiah (Dune Chronicles, #2)").ShouldBe(("dune", "messiah"));
    }
}

public class PersonNamesTests
{
    [Theory]
    // initials, inversion, dates
    [InlineData("dotted initials", "J.R.R. Tolkien", "jrr tolkien", "tolkien")]
    [InlineData("spaced initials", "J. R. R. Tolkien", "jrr tolkien", "tolkien")]
    [InlineData("inverted with dates", "Tolkien, J. R. R. 1892-1973.", "jrr tolkien", "tolkien")]
    [InlineData("accents fold", "Gabriel García Márquez", "gabriel garcia marquez", "marquez")]
    [InlineData("IN-065 suffix is never the surname", "Kurt Vonnegut Jr.", "kurt vonnegut jr", "vonnegut")]
    [InlineData("inverted with suffix un-inverts", "Vonnegut, Kurt, Jr.", "kurt vonnegut jr", "vonnegut")]
    [InlineData("comma before suffix is not an inversion", "Kurt Vonnegut, Jr.", "kurt vonnegut jr", "vonnegut")]
    [InlineData("DA-021 honorific dropped from the key", "Dr. Seuss", "seuss", "seuss")]
    [InlineData("IN-028 elided particle", "Madeleine L'Engle", "madeleine engle", "engle")]
    [InlineData("Irish O' joins", "Flannery O'Connor", "flannery oconnor", "oconnor")]
    [InlineData("non-Latin name", "Лев Толстой", "лев толстои", "толстои")]
    [InlineData("Japanese name", "村上春樹", "村上春樹", "村上春樹")]
    public void KeyAndSurname(string @case, string name, string key, string surname)
    {
        _ = @case;
        PersonNames.Key(name).ShouldBe(key);
        PersonNames.Surname(name).ShouldBe(surname);
    }

    [Fact]
    public void Key_DuplicateAuthorRecordsShareAKey()
    {
        PersonNames.Key("Mark Twain").ShouldBe(PersonNames.Key("MARK TWAIN"));
        PersonNames.Key("Dr. Seuss").ShouldBe(PersonNames.Key("Seuss"));
    }

    [Fact]
    public void Tokens_UnInvertAndDropDates()
    {
        PersonNames.Tokens("Tolkien, J. R. R. 1892-1973.").ShouldBe(["j", "r", "r", "tolkien"]);
        PersonNames.Tokens("Dixon, Charles").ShouldBe(["charles", "dixon"]);
    }
}
