using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Reports;
using FindThatBook.Eval.Running;

namespace FindThatBook.Api.Tests.Golden;

/// <summary>
/// Golden replay regression: all 60 golden v2 queries through the API's composition root with the LLM
/// off, the judge off, recorded Open Library fixtures (tests/fixtures/golden), .env ignored and the network blocked.
/// A ranking or status regression fails CI without any live call. Floors come from the first passing run (see eval/README.md).
/// </summary>
public sealed class GoldenReplayTests
{
    // Floors (answerable = 57, status rows = 60), measured 2026-10-25 on fixtures re-recorded live (all with v3 fields):
    // top-1 56/57, top-5 56/57, status 56/60, match 41/41. Raise them when the pipeline improves; never lower one without
    // a written reason. History: 2026-10-24 (fixtures recorded by the research prototype, without the v3 fields) top-5 57, status 57, match 39/40. character-04
    // holden caulfield (LLM off): the re-recorded live data turns a former wrong match into an honest none (the only
    // evidence for The Catcher in the Rye is an Italian edition title); with the LLM on, the interpreter supplies the title.
    private const int MinTop1 = 56;
    private const int MinTop5 = 56;
    private const int MinStatus = 56;
    private const double MinMatchPrecision = 41.0 / 41;

    [Fact]
    public async Task GoldenV2_ReplayLlmOffJudgeOff_MeetsFloorsWithNoFalseMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataset = DatasetLoader.Load("golden", allowHeldOut: false);
        if (!Directory.Exists(RepoPaths.GoldenFixtures) || !Directory.EnumerateFiles(RepoPaths.GoldenFixtures, "search_*.json").Any())
            Assert.Skip("tests/fixtures/golden is empty: run `dotnet run --project tools/FindThatBook.Eval -- fixtures`");

        var first = await RunAsync(dataset, ct);
        var rows = first.Rows.Select(r => r.Row).ToList();
        var q = SummaryWriter.Quality(rows);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"golden v2 replay (LLM off): top-1 {q.Top1}, top-5 {q.Top5}, status {q.Status}, match precision {q.MatchPrecision}, coverage {q.Coverage}");

        rows.Where(r => r.Meta.Replay is { Misses.Count: > 0 }).Select(r => $"{r.Id}: {string.Join(" · ", r.Meta.Replay!.Misses)}")
            .ShouldBeEmpty("fixture missing: run `dotnet run --project tools/FindThatBook.Eval -- fixtures` (or `record` for live-only requests)");
        rows.Where(r => r.Meta.Replay is { Partial.Count: > 0 }).Select(r => $"{r.Id}: {string.Join(" · ", r.Meta.Replay!.Partial)}")
            .ShouldBeEmpty("a fixture is shorter than the request (re-record it)");
        rows.Where(r => r.Status == Outcomes.Error).Select(r => $"{r.Id}: {r.Error}").ShouldBeEmpty();
        rows.Single(r => r.Id == "none-04").ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(Outcomes.Rejected),
            r => r.ErrorCode.ShouldBe("validation_failed"));
        rows.Where(r => r.Pass.FalseMatch).Select(r => r.Id).ShouldBeEmpty("a 'match' on a no-answer row is a false positive");
        q.Top1.Hits.ShouldBeGreaterThanOrEqualTo(MinTop1, $"top-1 {q.Top1}");
        q.Top5.Hits.ShouldBeGreaterThanOrEqualTo(MinTop5, $"top-5 {q.Top5}");
        q.Status.Hits.ShouldBeGreaterThanOrEqualTo(MinStatus, $"status {q.Status}");
        (q.MatchPrecision.Value ?? 1).ShouldBeGreaterThanOrEqualTo(MinMatchPrecision, $"match precision {q.MatchPrecision}");

        // R12: identical inputs → identical output. A second run must agree row for row.
        var second = await RunAsync(dataset, ct);
        second.Rows.Select(Fingerprint).ShouldBe(first.Rows.Select(Fingerprint));
    }

    private static string Fingerprint(RowOutcome r) => $"{r.Row.Id} {r.Row.Status} {r.Row.Reason} {string.Join(",", r.Row.Top5.Select(s => $"{s.WorkId}:{s.Tier}"))}";

    private static async Task<RunResult> RunAsync(Dataset dataset, CancellationToken ct)
    {
        await using var host = await EvalHost.StartAsync(new EvalHostOptions
        {
            Catalog = CatalogMode.Replay,
            ReplaySources = [RepoPaths.GoldenFixtures],
            Provider = "none",
            Judge = false,
            BlockNetwork = true,
            LoadDotEnv = false,
            LogLevel = Microsoft.Extensions.Logging.LogLevel.Error,
        }, ct);
        return await new EvalRunner(host, new RunSettings { Concurrency = 4, MaxLlmCalls = 0 }).RunAsync(dataset, ct: ct);
    }
}
