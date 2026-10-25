using System.Net;
using System.Text.Json.Nodes;
using FindThatBook.Core.Ports;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Replay;

namespace FindThatBook.Api.Tests.Golden;

/// <summary>Replay matching: recorded research responses must answer the C# pipeline's requests by parameters, not URL bytes.</summary>
public sealed class ReplayCatalogTests : IDisposable
{
    private const string Fields = "key%2Ctitle%2Cauthor_name%2Cauthor_key%2Cedition_count%2Ccover_i";
    private readonly string _dir = Directory.CreateTempSubdirectory("ftb-replay-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Record(string name, string url, params JsonObject[] docs)
    {
        var body = new JsonObject { ["numFound"] = docs.Length + 100, ["docs"] = new JsonArray([.. docs]) };
        File.WriteAllText(Path.Combine(_dir, name + ".json"), new JsonObject { ["url"] = url, ["dt"] = 1.5, ["body"] = body }.ToJsonString());
    }

    private static JsonObject Doc(string id, string title = "The Hobbit") => new()
    {
        ["key"] = $"/works/{id}", ["title"] = title, ["author_name"] = new JsonArray("J.R.R. Tolkien"),
        ["author_key"] = new JsonArray("OL26320A"), ["edition_count"] = 482, ["cover_i"] = -1, ["first_publish_year"] = 0,
        ["person"] = new JsonArray("Bilbo Baggins"),
    };

    [Theory] // Python's urlencode (research caches) vs the CatalogQuery the C# pipeline builds
    [InlineData("https://openlibrary.org/search.json?q=tolkien+hobbit&fields=" + Fields + "&limit=20", "q=tolkien hobbit")]
    [InlineData("https://openlibrary.org/search.json?limit=20&q=tolkien%20hobbit&fields=title", "q=tolkien hobbit")]
    [InlineData("https://openlibrary.org/search.json?q=person%3A%28heathcliff+AND+moors%29&fields=key&limit=20", "q=person:(heathcliff AND moors)")]
    [InlineData("https://openlibrary.org/search.json?title=The+Hobbit&author=Tolkien&sort=editions&limit=10", "title=The Hobbit&author=Tolkien&sort=editions")]
    [InlineData("https://openlibrary.org/search.json?author_key=OL26320A&sort=editions&fields=key&limit=20", "author_key=OL26320A&sort=editions")]
    [InlineData("https://openlibrary.org/search.json?title=%E4%B8%89%E4%BD%93&limit=20", "title=三体")]
    public void Parse_RecordedUrl_NormalizesToTheSameKey(string url, string key) =>
        RequestKey.Parse(url)!.Key.Text.ShouldBe(key);

    [Theory]
    [InlineData("https://openlibrary.org/search/authors.json?q=dickens")]
    [InlineData("https://openlibrary.org/search.json?q=hobbit&offset=20")]
    [InlineData("https://openlibrary.org/works/OL27482W.json")]
    public void Parse_NotAComparableSearch_ReturnsNull(string url) => RequestKey.Parse(url).ShouldBeNull();

    [Fact]
    public void From_CatalogQuery_MatchesTheParsedUrlKey()
    {
        var q = new CatalogQuery { Title = "The  Hobbit ", Author = "Tolkien", Sort = CatalogSort.Editions, Limit = 20, IncludePersons = true };

        RequestKey.From(q).ShouldBe(RequestKey.Parse("https://openlibrary.org/search.json?title=The+Hobbit&author=Tolkien&sort=editions&limit=10")!.Key);
    }

    [Fact]
    public async Task Search_RecordedRequest_MapsDocsAndTruncatesToTheLimit()
    {
        Record("a", "https://openlibrary.org/search.json?q=tolkien+hobbit&limit=20", Doc("OL27482W"), Doc("OL219602W"), Doc("OL1W"));
        var catalog = new ReplayCatalog(RecordedResponseIndex.Load([_dir]));

        var r = await catalog.SearchAsync(new CatalogQuery { Q = "tolkien hobbit", Limit = 2 }, TestContext.Current.CancellationToken);

        r.Works.Select(w => w.WorkId).ShouldBe(["OL27482W", "OL219602W"]);
        r.NumFound.ShouldBe(103);
        r.Works[0].CoverId.ShouldBeNull();          // cover_i -1 → null
        r.Works[0].FirstPublishYear.ShouldBeNull(); // 0 → null (R5)
        r.Works[0].Persons.ShouldBeEmpty();         // person only for person: calls
        r.Stats.Attempts.ShouldBe(0);               // no HTTP happened
        catalog.Used.Single().MaxLimit.ShouldBe(2);
    }

    [Fact]
    public async Task Search_PersonCall_KeepsCharacterNames()
    {
        Record("p", "https://openlibrary.org/search.json?q=person%3A%28bilbo%29&fields=key%2Cperson&limit=20", Doc("OL27482W"));
        var catalog = new ReplayCatalog(RecordedResponseIndex.Load([_dir]));

        var r = await catalog.SearchAsync(new CatalogQuery { Q = "person:(bilbo)", IncludePersons = true }, TestContext.Current.CancellationToken);

        r.Works.Single().Persons.ShouldBe(["Bilbo Baggins"]);
    }

    [Fact]
    public async Task Search_NoRecording_ReturnsEmptyAndReportsReplayMissOnTheQuery()
    {
        var catalog = new ReplayCatalog(RecordedResponseIndex.Load([_dir]));
        var probe = new QueryProbe("typo-01");

        CatalogResponse r;
        using (EvalContext.Begin(probe))
            r = await catalog.SearchAsync(new CatalogQuery { Q = "tolkein hobbit" }, TestContext.Current.CancellationToken);

        r.Works.ShouldBeEmpty();
        probe.ReplayMisses.ShouldBe(["q=tolkein hobbit"]);
        catalog.Misses["q=tolkein hobbit"].ShouldBe(["typo-01"]);
    }

    [Fact]
    public void Find_SeveralRecordings_PrefersOneThatCoversTheLimitThenPersonThenExactCase()
    {
        Record("a-small", "https://openlibrary.org/search.json?q=hobbit&fields=key&limit=10", Doc("OL1W"));
        Record("b-big", "https://openlibrary.org/search.json?q=hobbit&fields=key&limit=20", Doc("OL2W"));
        Record("c-person", "https://openlibrary.org/search.json?q=hobbit&fields=key%2Cperson&limit=20", Doc("OL3W"));
        Record("d-case", "https://openlibrary.org/search.json?q=Hobbit&fields=key%2Cperson&limit=20", Doc("OL4W"));
        var index = RecordedResponseIndex.Load([_dir]);
        var key = new RequestKey("hobbit", null, null, null, null);

        Path.GetFileName(index.Find(key, 20, needPersons: false)!.File).ShouldBe("b-big.json");
        Path.GetFileName(index.Find(key, 20, needPersons: true)!.File).ShouldBe("c-person.json");
        Path.GetFileName(index.Find(key with { Q = "HOBBIT" }, 20, needPersons: false)!.File).ShouldNotBeNull(); // case-insensitive fallback
        Path.GetFileName(index.Find(key with { Q = "Hobbit" }, 20, needPersons: true)!.File).ShouldBe("d-case.json");
    }

    [Fact]
    public async Task Search_RecordingShorterThanTheLimit_IsUsedButReportedAsPartial()
    {
        Record("short", "https://openlibrary.org/search.json?q=hobbit&limit=10", Doc("OL1W"));
        var catalog = new ReplayCatalog(RecordedResponseIndex.Load([_dir]));
        var probe = new QueryProbe("title-01");

        using (EvalContext.Begin(probe))
            await catalog.SearchAsync(new CatalogQuery { Q = "hobbit", Limit = 20 }, TestContext.Current.CancellationToken);

        probe.ReplayPartial.Single().ShouldContain("recorded limit 10, asked 20");
    }

    [Fact]
    public void IndexKey_ValueContainingAnAmpersand_NeverCollidesWithTwoParameters()
    {
        var glued = new RequestKey("x&title=y", null, null, null, null);
        var split = new RequestKey("x", "y", null, null, null);

        glued.Text.ShouldBe(split.Text); // the display text can look the same…
        glued.IndexKey.ShouldNotBe(split.IndexKey); // …the lookup key cannot
    }

    [Fact]
    public void Find_SmallerRecordingThatReturnedEverything_StillCoversTheLimit()
    {
        var url = "https://openlibrary.org/search.json?title=kokoro&author=soseki&limit=10";
        File.WriteAllText(Path.Combine(_dir, "k.json"), new JsonObject
        {
            ["url"] = url, ["body"] = new JsonObject { ["numFound"] = 1, ["docs"] = new JsonArray(Doc("OL1W", "Kokoro")) },
        }.ToJsonString());

        RecordedResponseIndex.Load([_dir]).Find(new RequestKey(null, "kokoro", "soseki", null, null), 20, false)!.Covers(20).ShouldBeTrue();
    }

    [Fact]
    public void Load_SkipsManifestsErrorBodiesAndOtherEndpoints()
    {
        File.WriteAllText(Path.Combine(_dir, FixtureWriter.ManifestName), "{\"files\":[]}");
        File.WriteAllText(Path.Combine(_dir, "err.json"), "{\"url\":\"https://openlibrary.org/search.json?q=x\",\"body\":{\"_error\":503,\"docs\":[]}}");
        Record("authors", "https://openlibrary.org/search/authors.json?q=dickens", Doc("OL1W"));
        Record("ok", "https://openlibrary.org/search.json?q=x&limit=20", Doc("OL1W"));

        var index = RecordedResponseIndex.Load([_dir]);

        index.Count.ShouldBe(1);
        index.Skipped.ShouldBe(3);
    }

    [Fact]
    public async Task FixtureWriter_UsedRecording_IsTrimmedAndReplaysIdentically()
    {
        Record("src", "https://openlibrary.org/search.json?q=tolkien+hobbit&limit=20", Doc("OL27482W"), Doc("OL219602W"), Doc("OL1W"));
        var catalog = new ReplayCatalog(RecordedResponseIndex.Load([_dir]));
        var query = new CatalogQuery { Q = "tolkien hobbit", Limit = 2 };
        var before = await catalog.SearchAsync(query, TestContext.Current.CancellationToken);
        var outDir = Path.Combine(_dir, "fixtures");

        FixtureWriter.Write(outDir, catalog.Used.Select(u => new FixtureEntry(u.Response, u.MaxLimit, ["golden-x"])), replace: true, new JsonObject());
        var after = await new ReplayCatalog(RecordedResponseIndex.Load([outDir])).SearchAsync(query, TestContext.Current.CancellationToken);

        System.Text.Json.JsonSerializer.Serialize(after.Works).ShouldBe(System.Text.Json.JsonSerializer.Serialize(before.Works));
        var fixture = Directory.EnumerateFiles(outDir, "search_*.json").Single();
        JsonNode.Parse(File.ReadAllText(fixture))!["body"]!["docs"]!.AsArray().Count.ShouldBe(2); // trimmed to the limit used
        File.Exists(Path.Combine(outDir, FixtureWriter.ManifestName)).ShouldBeTrue();
    }

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task RecordingHandler_SearchResponse_IsSavedReplayableAndStillReadableByTheCaller()
    {
        var body = new JsonObject { ["numFound"] = 1, ["docs"] = new JsonArray(Doc("OL27482W")) }.ToJsonString();
        var folder = Path.Combine(_dir, "rec");
        using var client = new HttpClient(new RecordingHandler(folder) { InnerHandler = new CannedHandler(HttpStatusCode.OK, body) });
        var probe = new QueryProbe("title-01");

        string read;
        using (EvalContext.Begin(probe))
            read = await client.GetStringAsync(new Uri("https://openlibrary.org/search.json?q=the+hobbit&fields=key,title&limit=20"), TestContext.Current.CancellationToken);

        read.ShouldBe(body);
        probe.Recorded.Count.ShouldBe(1);
        var replayed = new ReplayCatalog(RecordedResponseIndex.Load([folder]));
        var r = await replayed.SearchAsync(new CatalogQuery { Q = "the hobbit" }, TestContext.Current.CancellationToken);
        r.Works.Single().WorkId.ShouldBe("OL27482W");
    }

    [Theory]
    [InlineData("https://openlibrary.org/search.json?q=x", HttpStatusCode.ServiceUnavailable)] // failures are never fixtures
    [InlineData("https://generativelanguage.googleapis.com/v1/models:generate", HttpStatusCode.OK)] // only search.json is recorded
    public async Task RecordingHandler_OtherResponses_AreNotSaved(string url, HttpStatusCode status)
    {
        var folder = Path.Combine(_dir, "rec2");
        using var client = new HttpClient(new RecordingHandler(folder) { InnerHandler = new CannedHandler(status, "{\"docs\":[]}") });

        using var _ = await client.GetAsync(new Uri(url), TestContext.Current.CancellationToken);

        Directory.Exists(folder).ShouldBeFalse();
    }
}
