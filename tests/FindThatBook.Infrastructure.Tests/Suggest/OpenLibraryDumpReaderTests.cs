using System.IO.Compression;
using System.Text;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Infrastructure.Tests.Suggest;

/// <summary>The dump reader on tiny hand-written dumps in the real line format (type, key, revision, last_modified, JSON).</summary>
public sealed class OpenLibraryDumpReaderTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ftb-suggest-dumps-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Dumps_become_candidates_like_the_mirror()
    {
        WriteDump("ol_dump_editions_2026-09-30.txt.gz",
            Edition("OL1M", """{"works": [{"key": "/works/OL27482W"}], "publish_date": "September 21, 1937"}"""),
            Edition("OL2M", """{"publish_date": "1966", "title": "x", "works": [{"key": "/works/OL27482W"}, {"key": "/works/OL9W"}]}"""),
            Edition("OL3M", """{"works": [{"key": "/works/OL27482W"}], "publish_date": "n.d."}"""),
            Edition("OL4M", """{"works": [{"key": "/works/OL1168083W"}], "publish_date": "c1949"}"""),   // no word boundary: no year
            Edition("OL5M", """{"works": [{"key": "/works/OL1168083W"}], "publish_date": "1950-06"}"""),
            Edition("OL6M", """{"works": [{"key": "/works/OL5W"}]}"""),                               // below the minimum
            Edition("OL7M", """{"works": "not an array", "publish_date": "1900"}"""),
            Edition("OL8M", """{"works": [{"key": "/works/OL27482W"}, broken json"""),
            "/type/redirect\t/books/OL9M\t1\t2020-01-01T00:00:00\t{\"location\": \"/books/OL1M\"}");
        WriteDump("ol_dump_works_2026-09-30.txt.gz",
            Work("OL27482W", """{"title": "The Hobbit", "authors": [{"author": {"key": "/authors/OL404A"}}, {"author": {"key": "/authors/OL26320A"}, "type": {"key": "/type/author_role"}}], "covers": [-1, 14627509, 7]}"""),
            Work("OL1168083W", """{"title": "Nineteen Eighty-Four", "authors": [{"key": "/authors/OL15318546A"}]}"""),
            Work("OL5W", """{"title": "Rare", "authors": []}"""),
            Work("OL9W", """{"title": "Second work of a two-work edition"}"""));
        WriteDump("ol_dump_authors_2026-09-30.txt.gz",
            Author("OL404A", """{"name": ""}"""),                          // no name: the next author is primary
            Author("OL26320A", """{"name": "J.R.R. Tolkien"}"""),
            Author("OL15318546A", """{"personal_name": "George Orwell"}"""),
            Author("OL777A", """{"name": "Unwanted"}"""));

        var dumps = OpenLibraryDumpReader.Locate(dir);
        dumps.Date.ShouldBe("2026-09-30");
        var rows = OpenLibraryDumpReader.ReadCandidates(dumps, minEditions: 2, log: null).OrderBy(r => r.WorkId, StringComparer.Ordinal);

        rows.ShouldBe(
        [
            new SuggestRow("OL1168083W", "Nineteen Eighty-Four", "George Orwell", 1950, null, 2),
            new SuggestRow("OL27482W", "The Hobbit", "J.R.R. Tolkien", 1937, 14627509, 3),
        ]);
    }

    [Fact]
    public void Plain_dump_names_are_found_without_a_date()
    {
        foreach (var kind in new[] { "works", "editions", "authors" }) WriteDump($"{kind}.txt.gz");
        var dumps = OpenLibraryDumpReader.Locate(dir);
        dumps.Works.ShouldEndWith("works.txt.gz");
        dumps.Date.ShouldBeNull();
    }

    [Fact]
    public void A_missing_dump_is_reported() =>
        Should.Throw<FileNotFoundException>(() => OpenLibraryDumpReader.Locate(dir)).Message.ShouldContain("works");

    [Theory]
    [InlineData("/works/OL27482W", 27482)]
    [InlineData("/works/OL1W", 1)]
    [InlineData("/works/OL27482M", 0)]
    [InlineData("/books/OL27482W", 0)]
    [InlineData("/works/OLxW", 0)]
    [InlineData("/works/OL12a3W", 0)]
    public void Work_keys_parse_to_numbers(string key, int expected) =>
        OpenLibraryDumpReader.KeyNumber(Encoding.UTF8.GetBytes(key), "/works/OL"u8, (byte)'W').ShouldBe(expected);

    [Theory]
    [InlineData("September 21, 1937", 1937)]
    [InlineData("1603.", 1603)]
    [InlineData("[1866?]", 1866)]
    [InlineData("2023-04-01", 2023)]
    [InlineData("c1949", 0)]
    [InlineData("n.d.", 0)]
    [InlineData("12345", 0)]
    [InlineData("2150", 0)]
    public void First_year_matches_the_mirror_rule(string publishDate, int expected) =>
        OpenLibraryDumpReader.FirstYear(publishDate).ShouldBe(expected);

    void WriteDump(string name, params string[] lines)
    {
        using var file = File.Create(Path.Combine(dir, name));
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n"))));
    }

    static string Edition(string id, string json) => $"/type/edition\t/books/{id}\t3\t2021-01-01T00:00:00.000\t{json}";
    static string Work(string id, string json) => $"/type/work\t/works/{id}\t5\t2021-01-01T00:00:00.000\t{json}";
    static string Author(string id, string json) => $"/type/author\t/authors/{id}\t2\t2021-01-01T00:00:00.000\t{json}";
}
