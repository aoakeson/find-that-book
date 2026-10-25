using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Infrastructure.Tests.Suggest;

/// <summary>Lookup rules on a small hand-made index, independent of the committed data.</summary>
public sealed class SuggestIndexTests
{
    static readonly SuggestIndex Index = new(
    [
        Row("OL1W", "Harry Potter and the Philosopher's Stone", "J. K. Rowling", 400),
        Row("OL2W", "Harry Potter and the Chamber of Secrets", "J. K. Rowling", 306),
        Row("OL3W", "Harrow the Ninth", "Tamsyn Muir", 20),
        Row("OL4W", "The Hobbit", "J.R.R. Tolkien", 481),
        Row("OL5W", "The Lord of the Rings", "J.R.R. Tolkien", 300),
        Row("OL6W", "Tolkien: A Biography", "Humphrey Carpenter", 30),
        Row("OL7W", "Nineteen Eighty-Four", "George Orwell", 727),
        Row("OL8W", "Dune Messiah", "Frank Herbert", 101),
        Row("OL9W", "Dune", "Frank Herbert", 50),           // fewer editions than Dune Messiah, but an exact title
        Row("OL10W", "Hobbits of the Shire", "Some Fan", 481), // ties The Hobbit on editions: work id decides
        Row("OL11W", "Les Misérables", "Victor Hugo", 900),
        Row("OL12W", "三体", "刘慈欣", 44),
        Row("OL13W", "Анна Каренина", "Лев Толстой", 1316),
    ], "test");

    [Fact]
    public void Whole_title_prefix_ranks_by_edition_count() =>
        Ids(Index.Lookup("harr", 10)).ShouldBe(["OL1W", "OL2W", "OL3W"]);

    [Fact]
    public void Exact_title_comes_before_longer_titles() =>
        Ids(Index.Lookup("dune", 10)).ShouldBe(["OL9W", "OL8W"]);

    [Fact]
    public void One_letter_prefix_lists_titles_by_edition_count_then_the_rest()
    {
        var index = new SuggestIndex(
        [
            Row("OL1W", "S", "S. Livres", 19),                                    // exact one-letter title: no priority
            Row("OL2W", "The Scarlet Letter", "Nathaniel Hawthorne", 2425),
            Row("OL3W", "UK Continental Shelf Oil Well Records", "Rand McNally", 3760), // only a later word starts with s
            Row("OL4W", "Hamlet", "William Shakespeare", 2377),                    // only the author starts with s
        ], "test");
        Ids(index.Lookup("s", 10)).ShouldBe(["OL2W", "OL1W", "OL3W", "OL4W"]);
        Ids(index.Lookup("S", 10)).ShouldBe(["OL2W", "OL1W", "OL3W", "OL4W"]);
    }

    [Fact]
    public void Three_tiers_rank_in_order_each_by_edition_count()
    {
        var index = new SuggestIndex(
        [
            Row("OL21W", "Dune", "Frank Herbert", 50),                 // 1 exact title
            Row("OL22W", "Dune Messiah", "Frank Herbert", 40),         // 2 title prefix ending on a word
            Row("OL23W", "Children of Dune", "Frank Herbert", 30),     // 3 everything else, by editions:
            Row("OL24W", "Collected Poems", "Lord Dune", 35),          //   whole author word
            Row("OL25W", "Dunesday", "Someone", 900),                  //   partial title prefix
            Row("OL26W", "Sand Dunes", "Someone Else", 800),           //   partial title word
            Row("OL27W", "Poems", "Duneworth Smith", 700),             //   partial author name
        ], "test");

        index.Lookup("dune", 10).Select(s => (s.WorkId, s.Match)).ShouldBe(
        [
            ("OL21W", SuggestMatch.Title), ("OL22W", SuggestMatch.Title),
            ("OL25W", SuggestMatch.Title), ("OL26W", SuggestMatch.Word), ("OL27W", SuggestMatch.Author),
            ("OL24W", SuggestMatch.Author), ("OL23W", SuggestMatch.Word),
        ]);
    }

    [Fact]
    public void Mid_word_title_beats_a_whole_word_author_with_fewer_editions()
    {
        // Typing is usually mid-word: "hobb" is on its way to "hobbit", not only Robin Hobb's surname.
        var index = new SuggestIndex(
        [
            Row("OL61W", "Assassin's Apprentice", "Robin Hobb", 90),
            Row("OL62W", "Royal Assassin", "Robin Hobb", 70),
            Row("OL63W", "The Hobbit", "J.R.R. Tolkien", 481),
        ], "test");

        index.Lookup("hobb", 10).Select(s => (s.WorkId, s.Match)).ShouldBe(
            [("OL63W", SuggestMatch.Title), ("OL61W", SuggestMatch.Author), ("OL62W", SuggestMatch.Author)]);
    }

    [Fact]
    public void Title_words_titles_and_authors_compete_on_edition_count()
    {
        var index = new SuggestIndex(
        [
            Row("OL31W", "Potterism", "Rose Macaulay", 63),
            Row("OL32W", "Pottery and Porcelain", "Frederick Litchfield", 24),
            Row("OL33W", "Harry Potter and the Philosopher's Stone", "J. K. Rowling", 400),
            Row("OL34W", "The Tale of Peter Rabbit", "Beatrix Potter", 159),
        ], "test");

        index.Lookup("potter", 10).Select(s => (s.WorkId, s.Match)).ShouldBe(
            [("OL33W", SuggestMatch.Word), ("OL34W", SuggestMatch.Author), ("OL31W", SuggestMatch.Title), ("OL32W", SuggestMatch.Title)]);
    }

    [Fact]
    public void Author_name_competes_with_title_words()
    {
        var index = new SuggestIndex(
        [
            Row("OL41W", "Pride and Prejudice", "Jane Austen", 4042),
            Row("OL42W", "Jane Austen: A Life", "Claire Tomalin", 79),
        ], "test");

        Ids(index.Lookup("austen", 10)).ShouldBe(["OL41W", "OL42W"]);   // her novel (author word) beats the biography (title word)
        Ids(index.Lookup("jane", 10)).ShouldBe(["OL42W", "OL41W"]);     // a title starting with the word still comes first
        Ids(index.Lookup("aust", 10)).ShouldBe(["OL41W", "OL42W"]);     // partial: by editions, author or title word alike
    }

    [Fact]
    public void Title_prefix_on_a_word_break_beats_a_partial_one()
    {
        var index = new SuggestIndex(
        [
            Row("OL51W", "Lordship and Bondage", "Someone", 900),
            Row("OL52W", "The Lord of the Rings", "J.R.R. Tolkien", 300),
        ], "test");

        Ids(index.Lookup("lord", 10)).ShouldBe(["OL52W", "OL51W"]);
        Ids(index.Lookup("lord of the", 10)).ShouldBe(["OL52W"]);
    }

    [Fact]
    public void Author_prefix_lists_the_most_edited_books_before_a_title_about_the_author()
    {
        // "tolk": Tolkien's own books (author matches) outrank Tolkien: A Biography (a title match with 30 editions).
        var result = Index.Lookup("tolk", 10);
        result.Select(s => (s.WorkId, s.Match)).ShouldBe(
            [("OL4W", SuggestMatch.Author), ("OL5W", SuggestMatch.Author), ("OL6W", SuggestMatch.Title)]);
    }

    [Fact]
    public void Word_prefix_finds_a_later_title_word()
    {
        var result = Index.Lookup("potter", 10);
        result.Select(s => (s.WorkId, s.Match)).ShouldBe([("OL1W", SuggestMatch.Word), ("OL2W", SuggestMatch.Word)]);
    }

    [Theory]
    [InlineData("chamber of sec", "OL2W")]     // several words, word-aligned
    [InlineData("harry potter and the ph", "OL1W")]
    [InlineData("the lord of th", "OL5W")]     // a half-typed stop word still matches
    [InlineData("lord of the", "OL5W")]
    [InlineData("rowl", "OL1W")]               // author surname
    [InlineData("jk rowl", "OL1W")]            // author initials joined, like the search name key
    public void Multi_word_and_author_prefixes(string prefix, string firstId) =>
        Index.Lookup(prefix, 10)[0].WorkId.ShouldBe(firstId);

    [Fact]
    public void A_work_appears_once_with_its_best_match()
    {
        // "hobbit": The Hobbit by title; Hobbits of the Shire by title too; no duplicates from its word keys.
        var result = Index.Lookup("hobbit", 10);
        Ids(result).ShouldBe(["OL4W", "OL10W"]);
        result.ShouldAllBe(s => s.Match == SuggestMatch.Title);
    }

    [Fact]
    public void Ties_on_edition_count_break_by_work_id() =>
        Ids(Index.Lookup("hob", 10)).ShouldBe(["OL10W", "OL4W"]); // both 481 editions; "OL10W" < "OL4W" ordinal

    [Theory]
    [InlineData("1984", "OL7W")]               // numeral alias: digits find the spelled title
    [InlineData("nineteen eigh", "OL7W")]
    [InlineData("les mise", "OL11W")]          // accents folded
    [InlineData("LES MISÉ", "OL11W")]
    [InlineData("miserables", "OL11W")]
    [InlineData("三", "OL12W")]                // DE-07: CJK prefix
    [InlineData("刘", "OL12W")]                // CJK author
    [InlineData("анна", "OL13W")]              // DE-07: Cyrillic prefix, any case
    [InlineData("АННА КАР", "OL13W")]
    [InlineData("карен", "OL13W")]
    [InlineData("толст", "OL13W")]
    public void Normalized_prefixes_find_their_work(string prefix, string firstId) =>
        Index.Lookup(prefix, 10)[0].WorkId.ShouldBe(firstId);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("?!?")]
    [InlineData("zzzz")]
    public void Empty_or_unmatched_prefix_gives_an_empty_list(string prefix) => Index.Lookup(prefix, 8).ShouldBeEmpty();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 9)]   // five "h" titles + four works by Humphrey, Herbert (2) and Hugo
    [InlineData(500, 9)]
    public void Limit_is_respected(int limit, int expected) => Index.Lookup("h", limit).Count.ShouldBe(expected);

    [Fact]
    public void Limit_is_capped_at_ten()
    {
        var many = new SuggestIndex(Enumerable.Range(1, 30).Select(i => Row($"OL{i}W", $"Saga {i}", "Writer", 100 - i)), "test");
        many.Lookup("saga", 50).Count.ShouldBe(SuggestLimits.MaxLimit);
        Ids(many.Lookup("saga", 3)).ShouldBe(["OL1W", "OL2W", "OL3W"]);
    }

    [Fact]
    public void Prefix_longer_than_the_cap_is_cut_not_rejected()
    {
        var longPrefix = "harry potter and the philosopher's stone" + new string(' ', 200) + "zzz";
        Index.Lookup(longPrefix, 8)[0].WorkId.ShouldBe("OL1W");
    }

    [Fact]
    public void Suggestion_carries_the_display_fields()
    {
        var s = Index.Lookup("1984", 1).ShouldHaveSingleItem();
        s.ShouldBe(new Suggestion("OL7W", "Nineteen Eighty-Four", "George Orwell", 1949, 9267242, 727, SuggestMatch.Title));
    }

    [Fact]
    public void Lookup_is_deterministic() =>
        Index.Lookup("h", 10).ShouldBe(Index.Lookup("h", 10));

    static SuggestRow Row(string id, string title, string author, int editions) =>
        new(id, title, author, id == "OL7W" ? 1949 : null, id == "OL7W" ? 9267242 : null, editions);

    static string[] Ids(IReadOnlyList<Suggestion> s) => [.. s.Select(x => x.WorkId)];
}
