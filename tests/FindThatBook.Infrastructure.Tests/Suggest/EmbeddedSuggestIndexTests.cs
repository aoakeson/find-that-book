using System.Diagnostics;
using System.Text.RegularExpressions;
using FindThatBook.Infrastructure.Suggest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FindThatBook.Infrastructure.Tests.Suggest;

/// <summary>The committed index (suggest-index.json.gz, embedded): its content and the documented examples on real data.</summary>
public sealed class EmbeddedSuggestIndexTests
{
    static SuggestIndex Index => SuggestIndex.Embedded;

    [Fact]
    public void Embedded_index_holds_the_top_50k_works()
    {
        Index.Count.ShouldBe(50_000);
        Index.Version.ShouldBe("1-2026-09-30");
    }

    [Theory]
    [InlineData("1984", "OL1168083W")]                                // numeral alias → Nineteen Eighty-Four
    [InlineData("nineteen eigh", "OL1168083W")]
    [InlineData("三体", "OL17267881W")]                                // DE-07: CJK
    [InlineData("Анна Каренина", "OL267096W")]                        // DE-07: Cyrillic
    [InlineData("анна кар", "OL267096W")]
    [InlineData("le petit prince", "OL10263W")]                       // DE-07: French
    [InlineData("LE PETIT PR", "OL10263W")]
    [InlineData("les mise", "OL1063588W")]                            // Les Misérables: accents folded
    [InlineData("LES MISÉRABLES", "OL1063588W")]
    [InlineData("the hobbit", "OL27482W")]
    [InlineData("hob", "OL27482W")]                                   // leading article ignored
    [InlineData("hobb", "OL27482W")]                                  // mid-word title before Robin Hobb's books
    [InlineData("tolk", "OL27482W")]                                  // Tolkien's most-edited book before the Carpenter biography
    [InlineData("lord of the", "OL27448W")]                           // title prefix ending on a word
    [InlineData("lord of the r", "OL27448W")]
    [InlineData("austen", "OL66554W")]                                // her novel (author word) before books titled "Jane Austen…"
    [InlineData("it", "OL81613W")]                                    // exact title "It" before It Can't Happen Here
    [InlineData("s", "OL455305W")]                                    // one letter: by editions (The Scarlet Letter), not the "S" notebooks
    [InlineData("pride & prej", "OL66554W")]
    [InlineData("harry potter and the philosopher’s", "OL82563W")]    // curly apostrophe
    public void Known_prefixes_find_the_canonical_work_first(string prefix, string workId) =>
        Index.Lookup(prefix, SuggestLimits.DefaultLimit)[0].WorkId.ShouldBe(workId);

    [Fact]
    public void Whole_word_finds_harry_potter_before_potterism()
    {
        var result = Index.Lookup("potter", SuggestLimits.DefaultLimit);
        result[0].ShouldSatisfyAllConditions(
            () => result[0].WorkId.ShouldBe("OL82563W"),                  // Philosopher's Stone
            () => result[0].Match.ShouldBe(SuggestMatch.Word));
        result.Count(s => s.Author == "J. K. Rowling").ShouldBeGreaterThanOrEqualTo(6);
        result.ShouldNotContain(s => s.Title == "Potterism");             // 63 editions: below the Harry Potter books
    }

    [Fact]
    public void Author_prefix_finds_tolkiens_top_works_in_edition_order()
    {
        var result = Index.Lookup("tolk", SuggestLimits.MaxLimit);
        result.Select(s => s.WorkId).Take(3).ShouldBe(["OL27482W", "OL27513W", "OL27479W"]); // Hobbit, Fellowship, Two Towers
        var byAuthor = result.Where(s => s.Match == SuggestMatch.Author).ToArray();
        byAuthor.ShouldAllBe(s => s.Author == "J.R.R. Tolkien");
        byAuthor.Select(s => s.EditionCount).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void Rows_are_unique_ranked_and_clean()
    {
        var rows = ReadRows();
        rows.Count.ShouldBe(50_000);
        rows.Select(r => r.WorkId).Distinct().Count().ShouldBe(rows.Count);
        rows.ShouldAllBe(r => Regex.IsMatch(r.WorkId, @"^OL\d+W$"));
        rows.Select(r => SuggestText.DedupeKey(r.Title, r.Author)).Distinct().Count().ShouldBe(rows.Count);
        rows.Where(SuggestIndexBuilder.IsLowContent).ShouldBeEmpty();
        rows.Select(r => r.EditionCount).ShouldBeInOrder(SortDirection.Descending);
        rows.ShouldAllBe(r => r.Title.Trim() == r.Title && r.Title.Length > 0 && r.FirstPublishYear != 0);
    }

    [Fact]
    public void Thousand_lookups_are_fast()
    {
        string[] prefixes = ["a", "s", "the", "har", "potter", "tolk", "1984", "三", "анна", "lord of th"];
        foreach (var p in prefixes) Index.Lookup(p, 10); // warm
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++) Index.Lookup(prefixes[i % prefixes.Length], SuggestLimits.MaxLimit);
        watch.Stop();
        // Typically ~0.1 ms each; the bound is 2 ms average so a loaded CI box never flakes (plan target: < 5 ms).
        watch.ElapsedMilliseconds.ShouldBeLessThan(2_000);
    }

    [Fact]
    public void AddSuggest_registers_the_embedded_index_and_its_warmup()
    {
        var services = new ServiceCollection().AddSuggest(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<ISuggestIndex>().ShouldBeSameAs(SuggestIndex.Embedded);
        provider.GetServices<IHostedService>().ShouldContain(s => s is SuggestIndexWarmup);
    }

    static IReadOnlyList<SuggestRow> ReadRows()
    {
        using var stream = typeof(SuggestIndex).Assembly.GetManifestResourceStream("FindThatBook.Infrastructure.Suggest.suggest-index.json.gz")!;
        return SuggestIndexFile.Read(stream).Works;
    }
}
