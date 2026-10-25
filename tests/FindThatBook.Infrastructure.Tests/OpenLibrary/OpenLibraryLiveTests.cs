using System.Diagnostics;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>
/// Opt-in smoke check against live openlibrary.org: `FTB_LIVE=1 dotnet test ...`. Skipped otherwise. At most 10
/// HTTP requests, at 1 req/s, with the production User-Agent; confirms the fields, the 422 behaviour and caching.
/// </summary>
public sealed class OpenLibraryLiveTests(ITestOutputHelper output)
{
    private const int RequestBudget = 10;

    [Fact]
    public async Task Live_open_library_smoke()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FTB_LIVE") == "1", "Live Open Library check; set FTB_LIVE=1 to run.");
        var ct = TestContext.Current.CancellationToken;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenLibrary:RequestsPerSecond"] = "1",
            ["OpenLibrary:QueueLimit"] = "5",
            ["OpenLibrary:MaxRetries"] = "1",
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddOpenLibrary(configuration).BuildServiceProvider();
        var catalog = services.GetRequiredService<IBookCatalog>();
        services.GetRequiredService<HybridCache>().ShouldNotBeNull();
        var sent = 0;

        async Task<CatalogResponse?> Search(string label, CatalogQuery query)
        {
            if (sent + 3 > RequestBudget)
            {
                output.WriteLine($"{label}: skipped (request budget)");
                return null;
            }
            var clock = Stopwatch.StartNew();
            try
            {
                var response = await catalog.SearchAsync(query, ct);
                sent += response.Stats.Attempts;
                output.WriteLine($"{label}: numFound={response.NumFound} docs={response.Works.Count} {response.Stats} wall={clock.ElapsedMilliseconds} ms");
                foreach (var w in response.Works.Take(3))
                {
                    output.WriteLine($"   {w.WorkId} '{w.Title}' by {string.Join(", ", w.AuthorNames)} fpy={w.FirstPublishYear} eds={w.EditionCount} " +
                        $"cover={w.CoverId} editionTitles=[{string.Join(" | ", w.EditionTitles)}] alt={w.AuthorAlternativeNames.Count} persons={w.Persons.Count}");
                }
                return response;
            }
            catch (CatalogException ex)
            {
                sent += ex.Attempts;
                output.WriteLine($"{label}: {ex.GetType().Name} attempts={ex.Attempts} '{ex.Message}' wall={clock.ElapsedMilliseconds} ms");
                throw;
            }
        }

        var hobbit = await Search("q=tolkien hobbit", new CatalogQuery { Q = "tolkien hobbit" });
        hobbit!.Works.Take(5).ShouldContain(w => w.WorkId == "OL27482W");
        hobbit.Works.ShouldContain(w => w.EditionTitles.Count > 0);           // editions.title is served
        hobbit.Works.ShouldContain(w => w.AuthorAlternativeNames.Count > 0);  // author_alternative_name is served

        var cached = await catalog.SearchAsync(new CatalogQuery { Q = "tolkien hobbit" }, ct);
        cached.Stats.CacheHit.ShouldBeTrue();
        cached.Stats.Attempts.ShouldBe(0);

        var rejected = await Should.ThrowAsync<CatalogQueryRejectedException>(() => Search("q=ab (DE-05: expect 422)", new CatalogQuery { Q = "ab" }));
        rejected.Attempts.ShouldBe(1);

        var santi = await Search("title=三体", new CatalogQuery { Title = "三体" });
        santi?.Works.ShouldContain(w => w.WorkId == "OL17267881W");

        var dickens = await Search("author_key=OL24638A&sort=editions", new CatalogQuery { AuthorKey = "OL24638A", Sort = CatalogSort.Editions, Limit = 10 });
        dickens?.Works.ShouldAllBe(w => w.AuthorKeys.Contains("OL24638A"));

        var atticus = await Search("q=person:(atticus) +person", new CatalogQuery { Q = "person:(atticus)", IncludePersons = true, Limit = 10 });
        atticus?.Works.ShouldContain(w => w.Persons.Count > 0);

        output.WriteLine($"HTTP requests sent: {sent}");
        sent.ShouldBeLessThanOrEqualTo(RequestBudget);
    }
}
