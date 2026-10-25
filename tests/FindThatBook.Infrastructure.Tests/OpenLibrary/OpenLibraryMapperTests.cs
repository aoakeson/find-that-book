using System.Text.Json;
using FindThatBook.Core.Model;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

public sealed class OpenLibraryMapperTests
{
    private const int ThisYear = 2026;

    private static SearchPage Map(string json) =>
        OpenLibraryMapper.ToPage(JsonSerializer.Deserialize<OlSearchResponse>(json, OlSearchResponse.Json)!, ThisYear);

    private static CandidateWork One(string doc) => Map($"{{\"numFound\":1,\"docs\":[{doc}]}}").Works.ShouldHaveSingleItem();

    [Fact]
    public void Full_doc_maps_every_field()
    {
        var work = One("""
            {"key":"/works/OL17267881W","title":"三体","subtitle":"地球往事","author_name":["刘慈欣","Ken Liu"],
             "author_key":["OL7044246A","/authors/OL4094327A"],"contributor":["Liu, Ken, 1976- translator"],
             "first_publish_year":2008,"edition_count":45,"cover_i":9157544,"readinglog_count":1588,
             "author_alternative_name":["Cixin Liu","Liu Cixin"],"person":["Ye Wenjie","Wang Miao"],
             "editions":{"numFound":3,"docs":[{"title":"The Three-Body Problem"}]}}
            """);

        work.WorkId.ShouldBe("OL17267881W");
        work.Title.ShouldBe("三体");
        work.Subtitle.ShouldBe("地球往事");
        work.AuthorNames.ShouldBe(["刘慈欣", "Ken Liu"]);
        work.AuthorKeys.ShouldBe(["OL7044246A", "OL4094327A"]);
        work.Contributors.ShouldBe(["Liu, Ken, 1976- translator"]);
        work.FirstPublishYear.ShouldBe(2008);
        work.EditionCount.ShouldBe(45);
        work.CoverId.ShouldBe(9157544);
        work.ReadingLogCount.ShouldBe(1588);
        work.EditionTitles.ShouldBe(["The Three-Body Problem"]);
        work.AuthorAlternativeNames.ShouldBe(["Cixin Liu", "Liu Cixin"]);
        work.Persons.ShouldBe(["Ye Wenjie", "Wang Miao"]);
    }

    [Fact]
    public void Minimal_doc_gets_safe_defaults()
    {
        var work = One("""{"key":"/works/OL1W","title":"Untitled draft"}""");

        work.Subtitle.ShouldBeNull();
        work.AuthorNames.ShouldBeEmpty();
        work.AuthorKeys.ShouldBeEmpty();
        work.Contributors.ShouldBeEmpty();
        work.EditionTitles.ShouldBeEmpty();
        work.AuthorAlternativeNames.ShouldBeEmpty();
        work.Persons.ShouldBeEmpty();
        work.FirstPublishYear.ShouldBeNull();
        work.CoverId.ShouldBeNull();
        work.EditionCount.ShouldBe(0);
        work.ReadingLogCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"key":"/works/OL1W","title":"A","cover_i":-1}""", "cover_i -1 (works covers[] quirk) means no cover")]
    [InlineData("""{"key":"/works/OL1W","title":"A","cover_i":0}""", "cover 0 means no cover")]
    [InlineData("""{"key":"/works/OL1W","title":"A","author_name":null,"cover_i":null,"editions":null}""", "explicit nulls")]
    public void No_cover_cases(string doc, string because) => One(doc).CoverId.ShouldBeNull(because);

    [Theory]
    [InlineData(0, null)]    // DA-061 War of the Worlds OL52114W
    [InlineData(-200, null)]
    [InlineData(1937, 1937)]
    [InlineData(1800, 1800)] // a common bad-data year, but a real one: left to ranking and explanations
    [InlineData(2027, 2027)] // announced for next year: plausible
    [InlineData(2028, null)] // after next year: a data error
    [InlineData(20015, null)]
    public void DA_061_implausible_first_publish_year(int raw, int? expected) =>
        One($$"""{"key":"/works/OL1W","title":"A","first_publish_year":{{raw}}}""").FirstPublishYear.ShouldBe(expected);

    [Theory]
    [InlineData("/works/OL27482W", "OL27482W")]
    [InlineData("OL27482W", "OL27482W")]
    [InlineData("/authors/OL26320A", "OL26320A")]
    [InlineData("/works/OL17318741M", "OL17318741M")] // DA-059 orphan edition keeps its M id
    [InlineData(" /works/OL5W/ ", "OL5W")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Bare_keys(string? key, string? expected) => OpenLibraryMapper.BareKey(key).ShouldBe(expected);

    [Fact]
    public void Docs_without_key_or_title_are_dropped_and_null_entries_skipped()
    {
        var page = Map("""
            {"numFound":4,"docs":[{"title":"No key"},{"key":"/works/OL2W"},null,{"key":"/works/OL3W","title":"Kept",
             "author_name":["A",null,"  ","B"],"contributor":[null]}]}
            """);

        var work = page.Works.ShouldHaveSingleItem();
        work.WorkId.ShouldBe("OL3W");
        work.AuthorNames.ShouldBe(["A", "B"]);
        work.Contributors.ShouldBeEmpty();
        page.NumFound.ShouldBe(4); // numFound is Open Library's total, not the docs we kept
    }

    [Fact]
    public void The_best_matching_editions_key_is_kept_bare()
    {
        var work = One("""
            {"key":"/works/OL13476400W","title":"The Book of Mormon","editions":{"docs":[{"key":"/books/OL25760876M","title":"The Book of Mormon"}]}}
            """);
        work.EditionKey.ShouldBe("OL25760876M");
        One("""{"key":"/works/OL1W","title":"A"}""").EditionKey.ShouldBeNull();
    }

    [Fact]
    public void Edition_titles_are_distinct_and_capped_at_three()
    {
        var work = One("""
            {"key":"/works/OL1W","title":"A","editions":{"docs":[{"title":"One"},{"title":"One"},{"title":null},{"title":"Two"},
             {"title":"Three"},{"title":"Four"}]}}
            """);

        work.EditionTitles.ShouldBe(["One", "Two", "Three"]);
    }

    [Fact]
    public void Alternative_names_are_distinct_and_capped_at_sixty()
    {
        var names = string.Join(',', Enumerable.Range(0, 80).Select(i => $"\"Name {i % 70}\""));
        var work = One($$"""{"key":"/works/OL1W","title":"A","author_alternative_name":[{{names}}]}""");

        work.AuthorAlternativeNames.Count.ShouldBe(OpenLibraryMapper.MaxAlternativeNames);
        work.AuthorAlternativeNames.ShouldBeUnique();
    }

    [Fact]
    public void Subjects_are_trimmed_distinct_ignoring_case_and_capped_in_source_order()
    {
        var many = string.Join(',', Enumerable.Range(0, 50).Select(i => $"\"Subject {i}\""));
        var work = One($$"""
            {"key":"/works/OL279316W","title":"Little Bear","subject":[" Bears ","bears","",null,"   ","Teddy bears","BEARS","Juvenile fiction",{{many}}]}
            """);

        work.Subjects.Count.ShouldBe(OpenLibraryMapper.MaxSubjects);
        work.Subjects.Take(4).ShouldBe(["Bears", "Teddy bears", "Juvenile fiction", "Subject 0"]); // first spelling wins
        work.Subjects[^1].ShouldBe("Subject 36");
    }

    [Fact]
    public void No_subject_field_means_no_subjects() => One("""{"key":"/works/OL1W","title":"A"}""").Subjects.ShouldBeEmpty();

    [Fact]
    public void Numbers_sent_as_strings_still_parse()
    {
        var work = One("""{"key":"/works/OL1W","title":"A","edition_count":"12","first_publish_year":"1999"}""");

        work.EditionCount.ShouldBe(12);
        work.FirstPublishYear.ShouldBe(1999);
    }

    [Fact]
    public void Missing_docs_and_count_is_an_empty_page()
    {
        var page = Map("{}");

        page.Works.ShouldBeEmpty();
        page.NumFound.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Fixtures.Names), MemberType = typeof(Fixtures))]
    public void Every_recorded_fixture_maps_cleanly(string name)
    {
        var fixture = Fixtures.Named(name);
        var page = Map(fixture.BodyJson);

        page.NumFound.ShouldBe(fixture.NumFound);
        page.Works.Count.ShouldBe(fixture.Docs);
        page.Works.ShouldAllBe(w => !w.WorkId.Contains('/') && w.Title.Length > 0);
        page.Works.ShouldAllBe(w => w.CoverId == null || w.CoverId > 0);
        page.Works.ShouldAllBe(w => w.FirstPublishYear == null || w.FirstPublishYear > 0);
        page.Works.ShouldAllBe(w => w.EditionTitles.Count <= OpenLibraryMapper.MaxEditionTitles);
    }
}
