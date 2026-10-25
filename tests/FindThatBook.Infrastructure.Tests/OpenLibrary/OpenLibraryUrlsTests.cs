using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

public sealed class OpenLibraryUrlsTests
{
    private const string Fields = OpenLibraryUrls.BaseFields + "," + OpenLibraryUrls.V3Fields;

    public static TheoryData<string, CatalogQuery, string> Shapes => new()
    {
        { "broad q", new CatalogQuery { Q = "tolkien hobbit" }, $"search.json?q=tolkien%20hobbit&fields={Fields}&limit=20" },
        { "person: syntax passes through, person field requested", new CatalogQuery { Q = "person:(harry AND potter)", IncludePersons = true },
            $"search.json?q=person%3A%28harry%20AND%20potter%29&fields={Fields},person&limit=20" },
        { "OR-joined q", new CatalogQuery { Q = "tolkein OR hobbit" }, $"search.json?q=tolkein%20OR%20hobbit&fields={Fields}&limit=20" },
        { "topic: subject:(…) syntax passes through, subject field requested", new CatalogQuery { Q = "subject:(teddy AND bears)", IncludeSubjects = true },
            $"search.json?q=subject%3A%28teddy%20AND%20bears%29&fields={Fields},subject&limit=20" },
        { "topic heading with its own parentheses", new CatalogQuery { Q = "subject:(paris (france))", IncludeSubjects = true },
            $"search.json?q=subject%3A%28paris%20%28france%29%29&fields={Fields},subject&limit=20" },
        { "suggestions: title:(…) OR title:(…)", new CatalogQuery { Q = "title:(winnie pooh) OR title:(paddington) OR title:(corduroy)", IncludeSubjects = true },
            $"search.json?q=title%3A%28winnie%20pooh%29%20OR%20title%3A%28paddington%29%20OR%20title%3A%28corduroy%29&fields={Fields},subject&limit=20" },
        { "topic with an author hint", new CatalogQuery { Q = "subject:(cats)", Author = "doris lessing", IncludeSubjects = true },
            $"search.json?q=subject%3A%28cats%29&author=doris%20lessing&fields={Fields},subject&limit=20" },
        { "person and subject fields together", new CatalogQuery { Q = "person:(pooh)", IncludePersons = true, IncludeSubjects = true },
            $"search.json?q=person%3A%28pooh%29&fields={Fields},person,subject&limit=20" },
        { "title= and author=", new CatalogQuery { Title = "three body problem", Author = "cixin" },
            $"search.json?title=three%20body%20problem&author=cixin&fields={Fields}&limit=20" },
        { "author_key + sort=editions (author top works)", new CatalogQuery { AuthorKey = "OL24638A", Sort = CatalogSort.Editions, Limit = 10 },
            $"search.json?author_key=OL24638A&sort=editions&fields={Fields}&limit=10" },
        { "author= + sort=editions", new CatalogQuery { Author = "stephen king", Sort = CatalogSort.Editions, Limit = 10 },
            $"search.json?author=stephen%20king&sort=editions&fields={Fields}&limit=10" },
        { "DE-05 CJK title as UTF-8", new CatalogQuery { Title = "三体" }, $"search.json?title=%E4%B8%89%E4%BD%93&fields={Fields}&limit=20" },
        { "Hangul NFC sent as composed syllables", new CatalogQuery { Title = "채식주의자", Author = "한강" },
            $"search.json?title=%EC%B1%84%EC%8B%9D%EC%A3%BC%EC%9D%98%EC%9E%90&author=%ED%95%9C%EA%B0%95&fields={Fields}&limit=20" },
        { "ampersand and plus are data, not separators", new CatalogQuery { Q = "pride & prejudice c++" },
            $"search.json?q=pride%20%26%20prejudice%20c%2B%2B&fields={Fields}&limit=20" },
        { "blank values are not sent", new CatalogQuery { Q = "dune", Title = " ", Author = "" }, $"search.json?q=dune&fields={Fields}&limit=20" },
        { "values are trimmed", new CatalogQuery { Title = "  it  " }, $"search.json?title=it&fields={Fields}&limit=20" },
        { "DA-070 limit capped at 100", new CatalogQuery { Q = "dickens", Limit = 1000 }, $"search.json?q=dickens&fields={Fields}&limit=100" },
        { "limit at least 1", new CatalogQuery { Q = "dickens", Limit = 0 }, $"search.json?q=dickens&fields={Fields}&limit=1" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Search_url_for_every_query_shape(string because, CatalogQuery query, string expected) =>
        OpenLibraryUrls.Search(query).ShouldBe(expected, because);

    [Fact]
    public void No_search_terms_gives_no_url() =>
        OpenLibraryUrls.Search(new CatalogQuery { Title = "  ", Sort = CatalogSort.Editions }).ShouldBeNull();

    [Theory]
    [InlineData(20, 10)]
    [InlineData(5, 5)]
    public void Slow_empty_requery_is_lighter_but_keeps_the_query(int limit, int expectedLimit)
    {
        var url = OpenLibraryUrls.Search(new CatalogQuery { Q = "person:(atticus)", IncludePersons = true, Limit = limit }, slowEmptyRequery: true);

        url.ShouldBe($"search.json?q=person%3A%28atticus%29&fields={OpenLibraryUrls.BaseFields},person&limit={expectedLimit}");
        url!.ShouldNotContain("editions");
    }

    [Fact]
    public void Slow_empty_requery_keeps_the_subject_field() =>
        OpenLibraryUrls.Search(new CatalogQuery { Q = "subject:(bears)", IncludeSubjects = true }, slowEmptyRequery: true)
            .ShouldBe($"search.json?q=subject%3A%28bears%29&fields={OpenLibraryUrls.BaseFields},subject&limit=10");

    [Fact]
    public void Cache_key_covers_the_fields_list()
    {
        Uri Absolute(CatalogQuery q) => new(new Uri("https://openlibrary.org/"), OpenLibraryUrls.Search(q));
        var plain = new CatalogQuery { Q = "subject:(bears)" };

        OpenLibraryUrls.CacheKey(Absolute(plain with { IncludeSubjects = true })).ShouldNotBe(OpenLibraryUrls.CacheKey(Absolute(plain)));
        OpenLibraryUrls.CacheKey(Absolute(plain with { IncludePersons = true })).ShouldNotBe(OpenLibraryUrls.CacheKey(Absolute(plain)));
    }

    [Fact]
    public void Text_is_sent_exactly_as_given_no_normalization()
    {
        // CatalogQueries owns NFC. Note: with InvariantGlobalization=true, string.Normalize is a no-op for non-ASCII,
        // so decomposed jamo would stay decomposed whoever called it.
        var decomposed = "한"; // 한 as three jamo
        OpenLibraryUrls.Search(new CatalogQuery { Author = decomposed })
            .ShouldStartWith("search.json?author=%E1%84%92%E1%85%A1%E1%86%AB&");
    }

    [Fact]
    public void SY_23_cache_key_is_bounded_for_a_300_character_CJK_query()
    {
        var url = OpenLibraryUrls.Search(new CatalogQuery { Title = new string('体', 300) })!;
        var absolute = new Uri(new Uri("https://openlibrary.org/"), url);

        absolute.AbsoluteUri.Length.ShouldBeGreaterThan(1024);
        var key = OpenLibraryUrls.CacheKey(absolute);
        key.Length.ShouldBe(70);
        key.ShouldStartWith("ol:v1:");
        key.ShouldBe(OpenLibraryUrls.CacheKey(new Uri(absolute.AbsoluteUri)));
        key.ShouldNotBe(OpenLibraryUrls.CacheKey(new Uri("https://openlibrary.org/" + OpenLibraryUrls.Search(new CatalogQuery { Title = "体" }))));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8090", "http://127.0.0.1:8090/search.json")]
    [InlineData("http://mirror.local/ol", "http://mirror.local/ol/search.json")]
    [InlineData("https://openlibrary.org/", "https://openlibrary.org/search.json")]
    public void Base_url_keeps_its_path(string baseUrl, string expected) =>
        new Uri(OpenLibraryUrls.BaseAddress(baseUrl), "search.json").AbsoluteUri.ShouldBe(expected);

    [Theory]
    [InlineData("OL27482W", "https://openlibrary.org/works/OL27482W")]
    [InlineData("OL17318741M", "https://openlibrary.org/books/OL17318741M")] // DA-059 orphan edition
    public void Work_links_use_the_public_site(string id, string expected) =>
        OpenLibraryLinks.Work(new OpenLibraryOptions { BaseUrl = "http://127.0.0.1:8090/" }, id).ShouldBe(expected);

    [Theory]
    [InlineData(14627509, 'M', "https://covers.openlibrary.org/b/id/14627509-M.jpg")]
    [InlineData(-1, 'M', null)]
    [InlineData(null, 'L', null)]
    [InlineData(5, 'X', null)]
    public void Cover_links(int? coverId, char size, string? expected) =>
        OpenLibraryLinks.Cover(new OpenLibraryOptions(), coverId, size).ShouldBe(expected);
}
