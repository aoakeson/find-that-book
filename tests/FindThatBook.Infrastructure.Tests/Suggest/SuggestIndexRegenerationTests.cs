using System.Runtime.CompilerServices;
using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Infrastructure.Tests.Suggest;

/// <summary>
/// Opt-in (explicit) entry point that rewrites the committed src/FindThatBook.Infrastructure/Suggest/suggest-index.json.gz.
/// From the Open Library dumps (≈ 17 GB, minutes):
/// <code>FTB_SUGGEST_DUMPS=/path/to/dumps dotnet test --project tests/FindThatBook.Infrastructure.Tests --explicit only --filter-method "*Regenerate_from_dumps"</code>
/// From a candidates file extracted from the same dumps (seconds):
/// <code>FTB_SUGGEST_CANDIDATES=/path/suggest-100k.json.gz FTB_SUGGEST_DUMP_DATE=2026-09-30 dotnet test --project tests/FindThatBook.Infrastructure.Tests --explicit only --filter-method "*Regenerate_from_candidates"</code>
/// Optional: FTB_SUGGEST_OUT (another output path), FTB_SUGGEST_DUMP_DATE (when the dump file names carry no date),
/// FTB_SUGGEST_BUILT (build date, default today, yyyy-MM-dd). Then run the normal tests: they check the new file.
/// </summary>
public sealed class SuggestIndexRegenerationTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public void Regenerate_from_dumps()
    {
        var dumps = Environment.GetEnvironmentVariable("FTB_SUGGEST_DUMPS");
        Assert.SkipWhen(string.IsNullOrEmpty(dumps), "Set FTB_SUGGEST_DUMPS to the folder with the works, editions and authors dumps.");
        var report = SuggestIndexBuilder.BuildFromDumps(dumps!, OutputPath(), BuiltOn(),
            Environment.GetEnvironmentVariable("FTB_SUGGEST_DUMP_DATE"), log: output.WriteLine);
        Report(report);
    }

    [Fact(Explicit = true)]
    public void Regenerate_from_candidates()
    {
        var candidates = Environment.GetEnvironmentVariable("FTB_SUGGEST_CANDIDATES");
        var dumpDate = Environment.GetEnvironmentVariable("FTB_SUGGEST_DUMP_DATE");
        Assert.SkipWhen(string.IsNullOrEmpty(candidates) || string.IsNullOrEmpty(dumpDate),
            "Set FTB_SUGGEST_CANDIDATES (gzipped JSON rows) and FTB_SUGGEST_DUMP_DATE (yyyy-MM-dd of the dumps they came from).");
        Report(SuggestIndexBuilder.BuildFromCandidates(candidates!, OutputPath(), dumpDate!, BuiltOn()));
    }

    void Report(SuggestBuildReport r)
    {
        output.WriteLine($"{r.OutputPath}: {r.Count:N0} works, {r.Bytes:N0} bytes; from {r.Candidates:N0} candidates " +
                         $"(invalid {r.Invalid:N0}, low-content {r.LowContent:N0}, duplicates {r.Duplicates:N0}); min editions {r.MinEditionCount}");
        r.Count.ShouldBe(SuggestIndexBuilder.DefaultCount);
    }

    static DateOnly BuiltOn() =>
        Environment.GetEnvironmentVariable("FTB_SUGGEST_BUILT") is { Length: > 0 } built ? DateOnly.Parse(built, System.Globalization.CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Now);

    static string OutputPath([CallerFilePath] string thisFile = "") =>
        Environment.GetEnvironmentVariable("FTB_SUGGEST_OUT") is { Length: > 0 } custom ? custom
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "../../../src/FindThatBook.Infrastructure/Suggest/suggest-index.json.gz"));
}
