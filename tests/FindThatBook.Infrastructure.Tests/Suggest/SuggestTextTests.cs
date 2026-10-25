using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Infrastructure.Tests.Suggest;

public sealed class SuggestTextTests
{
    [Theory]
    [InlineData("Harry", "harry")]                                         // case
    [InlineData("  har  ", "har")]                                         // whitespace
    [InlineData("Les Misérables", "les miserables")]                      // accents
    [InlineData("CAFÉ Ł", "cafe l")]                                       // accent + folded letter
    [InlineData("philosopher’s", "philosophers")]                         // curly apostrophe joins, like search
    [InlineData("philosopher's st", "philosophers st")]                   // straight apostrophe
    [InlineData("“the hobbit”", "hobbit")]                                 // curly double quotes are punctuation; article dropped
    [InlineData("pride & prej", "pride and prej")]                         // & → and
    [InlineData("20,000 leagues", "20000 leagues")]                        // digit groups
    [InlineData("the lord of th", "lord of th")]                           // only the leading article goes; stop words stay
    [InlineData("a", "a")]                                                 // a lone article is the typing state, kept
    [InlineData("三体", "三体")]                                           // DE-07 CJK kept
    [InlineData("Анна Карен", "анна карен")]                               // DE-07 Cyrillic kept, lowercased
    [InlineData("?!? ###", "")]                                            // nothing searchable
    public void PrefixKey_folds_like_search(string prefix, string expected) =>
        SuggestText.PrefixKey(prefix).ShouldBe(expected);

    [Theory]
    [InlineData("Nineteen Eighty-Four", "nineteen eighty four|1984")]
    [InlineData("Twenty Thousand Leagues Under the Sea", "twenty thousand leagues under the sea|20000 leagues under the sea")]
    [InlineData("20,000 Leagues Under the Sea", "20000 leagues under the sea|twenty thousand leagues under the sea")]
    [InlineData("1984", "1984|nineteen eighty four")]
    [InlineData("Fahrenheit 451", "fahrenheit 451|fahrenheit four hundred fifty one")]
    [InlineData("The Hobbit", "hobbit")]
    [InlineData("Código civil (1984)", "codigo civil")]                    // a parenthesised year is not a "1984" title
    [InlineData("The Hobbit & The Lord of the Rings [collection/set]", "hobbit and the lord of the rings")]
    [InlineData("(Untitled)", "untitled")]                                 // all bracketed: keep the words
    [InlineData("三体", "三体")]
    public void TitleForms_include_numeral_aliases(string title, string expected) =>
        string.Join('|', SuggestText.TitleForms(title).Select(f => string.Join(' ', f))).ShouldBe(expected);

    [Fact]
    public void WordStarts_skip_the_first_word_and_stop_words()
    {
        string[] form = ["harry", "potter", "and", "the", "philosophers", "stone"];
        var text = string.Join(' ', form);
        SuggestText.WordStarts(form).Select(i => text[i..]).ShouldBe(["potter and the philosophers stone", "philosophers stone", "stone"]);
    }

    [Theory]
    [InlineData("J.R.R. Tolkien", "jrr tolkien|tolkien")]
    [InlineData("Jane Austen", "jane austen|austen")]
    [InlineData("Miguel de Cervantes Saavedra", "miguel de cervantes saavedra|de cervantes saavedra|cervantes saavedra|saavedra")]
    [InlineData("Лев Толстой", "лев толстои|толстои")]                     // й folds like search (NFKD drops the breve)
    [InlineData("", "")]
    public void AuthorKeys_cover_full_name_and_surname(string author, string expected) =>
        string.Join('|', SuggestText.AuthorKeys(author)).ShouldBe(expected);

    [Theory]
    [InlineData("Pride and Prejudice", "Jane Austen", "Pride & Prejudice", "Jane Austen")]
    [InlineData("Pride and Prejudice", "Jane Austen", "Pride and prejudice: a novel", "Austen, Jane")]
    [InlineData("Nineteen Eighty-Four", "George Orwell", "1984", "George Orwell")]
    [InlineData("The Hobbit", "J.R.R. Tolkien", "Hobbit", "J. R. R. Tolkien")]
    public void DedupeKey_matches_the_same_work(string title1, string author1, string title2, string author2) =>
        SuggestText.DedupeKey(title1, author1).ShouldBe(SuggestText.DedupeKey(title2, author2));

    [Theory]
    [InlineData("Hamlet", "William Shakespeare", "Hamlet", "Some Critic")]  // same title, other author: kept apart
    [InlineData("Dune", "Frank Herbert", "Dune Messiah", "Frank Herbert")]
    public void DedupeKey_keeps_different_works_apart(string title1, string author1, string title2, string author2) =>
        SuggestText.DedupeKey(title1, author1).ShouldNotBe(SuggestText.DedupeKey(title2, author2));

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(13, "thirteen")]
    [InlineData(42, "forty two")]
    [InlineData(101, "one hundred one")]
    [InlineData(451, "four hundred fifty one")]
    [InlineData(1200, "one thousand two hundred")]
    [InlineData(1776, "seventeen seventy six")]
    [InlineData(1984, "nineteen eighty four")]
    [InlineData(2001, "two thousand one")]
    [InlineData(20000, "twenty thousand")]
    [InlineData(999999, "nine hundred ninety nine thousand nine hundred ninety nine")]
    public void NumberWords_spells_numbers(int n, string expected) => NumberWords.ToWords(n).ShouldBe(expected);

    [Fact]
    public void NumberWords_round_trip_through_the_numeral_folding()
    {
        // Every spelled number must fold back to its digits, or the alias would never meet the search form.
        foreach (var n in Enumerable.Range(0, 10_000).Concat([20_000, 80_000, 100_000, 123_456]))
        {
            var form = SuggestText.TitleForms(NumberWords.ToWords(n)!);
            string.Join(' ', form[^1]).ShouldBe(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"n = {n}");
        }
    }

    [Theory]
    [InlineData("007")]         // leading zero: a code, not a number
    [InlineData("1st")]
    [InlineData("1234567")]     // out of range
    public void NumberWords_leaves_non_numbers_alone(string token) => NumberWords.Expand([token]).ShouldBe([token]);
}
