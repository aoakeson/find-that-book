using System.IO.Compression;
using System.Text;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Infrastructure.Tests.Suggest;

/// <summary>Build-time cleanup rules: tidy, drop low-content, rank, dedupe, cut.</summary>
public sealed class SuggestIndexBuilderTests
{
    static readonly DateOnly BuiltOn = new(2026, 10, 24);

    [Fact]
    public void Dedupe_by_title_key_and_author_keeps_the_most_editions()
    {
        var result = SuggestIndexBuilder.Build(
        [
            new("OL2W", "Pride & Prejudice", "Jane Austen", 1813, null, 30),
            new("OL1W", "Pride and Prejudice", "Jane Austen", 1813, 14348537, 4042),
            new("OL3W", "Pride and prejudice: a novel", "Austen, Jane", 1900, null, 12),
            new("OL4W", "Pride and Prejudice", "Some Adapter", 2001, null, 11),     // another author: another work
            new("OL5W", "Dune", "Frank Herbert", 1965, null, 155),
            new("OL6W", "Dune", "Frank Herbert", 1965, null, 40),
        ], "2026-09-30", BuiltOn);

        result.File.Works.Select(w => w.WorkId).ShouldBe(["OL1W", "OL5W", "OL4W"]);
        result.Duplicates.ShouldBe(3);
    }

    [Fact]
    public void Rows_are_ranked_by_edition_count_then_work_id_and_cut_to_count()
    {
        var rows = Enumerable.Range(1, 20).Select(i => new SuggestRow($"OL{i}W", $"Title {NumberWords.ToWords(i)}", "A", 1900, null, i % 5 + 10));
        var result = SuggestIndexBuilder.Build(rows, "2026-09-30", BuiltOn, count: 6);

        result.File.Works.Select(w => (w.WorkId, w.EditionCount)).ShouldBe(
            [("OL14W", 14), ("OL19W", 14), ("OL4W", 14), ("OL9W", 14), ("OL13W", 13), ("OL18W", 13)]);
    }

    [Fact]
    public void Fields_are_tidied_and_implausible_values_dropped()
    {
        var row = SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "  The   Hobbit\t", "  J.R.R.  Tolkien ", 0, -1, 481), maxYear: 2027)!;
        row.ShouldBe(new SuggestRow("OL1W", "The Hobbit", "J.R.R. Tolkien", null, null, 481));

        SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "Future", "X", 2999, 5, 10), maxYear: 2027)!
            .ShouldBe(new SuggestRow("OL1W", "Future", "X", null, 5, 10));
        SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "   ", "X", 1900, null, 10), 2027).ShouldBeNull();
        SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "Macmillan's magazine", null, 1860, null, 25), 2027).ShouldBeNull();
        SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "CDROM", " ", 1994, null, 23), 2027).ShouldBeNull();
        SuggestIndexBuilder.Clean(new SuggestRow("OL1W", "Zero", "X", 1900, null, 0), 2027).ShouldBeNull();
    }

    [Theory]
    [InlineData("Mileage Log Book", 2021, true)]
    [InlineData("Adult Coloring Journal", 2016, true)]
    [InlineData("Cuaderno", 2020, true)]
    [InlineData("Sudoku Puzzle Book", 2021, true)]
    [InlineData("Pocket Word Search", 2019, true)]
    [InlineData("Composition Notebook: Wide Ruled", 2018, true)]
    [InlineData("The Notebook", 1996, false)]           // a novel
    [InlineData("Diary of a Wimpy Kid", 2007, false)]
    [InlineData("The Diary of a Nobody", 1892, false)]
    [InlineData("Journal", null, false)]                // unknown year: kept
    [InlineData("Project Hail Mary", 2021, false)]
    public void Low_content_print_on_demand_is_dropped(string title, int? year, bool dropped) =>
        SuggestIndexBuilder.IsLowContent(new SuggestRow("OL1W", title, "X", year, null, 500)).ShouldBe(dropped);

    [Theory]
    [InlineData("Cat", "Pussycat Journal", 2017, true)]
    [InlineData("2018", "Planner N. Planner N Journal", 2017, true)]
    [InlineData("Cat", "The Little The Little Cat", 2017, true)]
    [InlineData("Cat", "BabyCat", 2017, true)]
    [InlineData("Cat", "Dr. Seuss", 2017, false)]
    [InlineData("Cat", "Pussycat Journal", 1990, false)]
    public void Modern_single_word_stubs_are_filtered_by_author(string title, string author, int year, bool dropped) =>
        SuggestIndexBuilder.IsLowContent(new SuggestRow("OL1W", title, author, year, null, 87)).ShouldBe(dropped);

    [Fact]
    public void Report_counts_what_was_dropped()
    {
        var result = SuggestIndexBuilder.Build(
        [
            new("OL1W", "Hamlet", "William Shakespeare", 1603, null, 2377),
            new("OL2W", "Mileage Log Book", "Motwani Dev", 2021, null, 2334),
            new("OL3W", "", "Nobody", 2000, null, 50),
            new("OL4W", "Hamlet", "Shakespeare, William", 1603, null, 20),
        ], "2026-09-30", BuiltOn);

        (result.Candidates, result.Invalid, result.LowContent, result.Duplicates, result.File.Works.Count).ShouldBe((4, 1, 1, 1, 1));
    }

    [Fact]
    public void File_round_trips_with_its_header()
    {
        var file = SuggestIndexBuilder.Build(
        [
            new("OL17267881W", "三体", "刘慈欣", 2006, 8231432, 44),
            new("OL267096W", "Анна Каренина", "Лев Толстой", 1876, null, 1316),
            new("OL1W", "Quote \"marks\" and \\ slashes", "O'Brien", null, null, 10),
        ], "2026-09-30", BuiltOn).File;

        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        var read = SuggestIndexFile.Read(stream);

        read.Works.ShouldBe(file.Works);
        (read.Format, read.DumpDate, read.Built, read.Version).ShouldBe((1, "2026-09-30", "2026-10-24", "1-2026-09-30"));
        read.Source.ShouldContain("CC0");

        stream.Position = 0;
        using var text = new StreamReader(new GZipStream(stream, CompressionMode.Decompress), Encoding.UTF8);
        var json = text.ReadToEnd();
        json.ShouldStartWith("{\"format\":1,");
        json.ShouldContain("\"count\":3,");
        json.ShouldContain("[\"OL267096W\",\"Анна Каренина\",\"Лев Толстой\",1876,null,1316]"); // readable, one row per line
        json.Split('\n').Length.ShouldBe(5);
    }

    [Fact]
    public void A_bad_dump_date_is_rejected() =>
        Should.Throw<ArgumentException>(() => SuggestIndexBuilder.Build([], "30/09/2026", BuiltOn));

    [Fact]
    public void Candidates_file_rows_are_read()
    {
        const string json = """
            [{"w":"OL27482W","t":"The Hobbit","a":"J.R.R. Tolkien","y":1937,"c":14627509,"e":481},
             {"w":"OL1W","t":"No author","a":null,"y":null,"c":null,"e":12}]
            """;
        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true)) z.Write(Encoding.UTF8.GetBytes(json));
        gz.Position = 0;

        SuggestIndexBuilder.ReadCandidatesFile(gz).ShouldBe(
        [
            new SuggestRow("OL27482W", "The Hobbit", "J.R.R. Tolkien", 1937, 14627509, 481),
            new SuggestRow("OL1W", "No author", null, null, null, 12),
        ]);
    }
}
