using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Reports;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Eval.Stability;

/// <summary>
/// Two separate variability measurements, both with caches bypassed:
/// interpretation = the same cleaned query sent to the interpreter N times (does the LLM give the same title/author?);
/// retrieval = the same query searched N times against Open Library with the LLM off by default (does Open Library
/// return the same pool and does our ranking pick the same works?).
/// </summary>
public static class StabilityCommand
{
    public sealed record Settings(int Runs, int? MaxLlmCalls, int? MaxCatalogRequests, int Concurrency, int? LlmCallsPerMinute = null);

    public static async Task<string> InterpretAsync(EvalHost host, Dataset dataset, Settings s, string outDir, string command, CancellationToken ct)
    {
        var queries = dataset.Cases.Select(c => (Case: c, Validation: QueryValidator.Validate(c.Query)))
            .Where(x => x.Validation.IsValid).Select(x => (x.Case, Clean: QueryCleaner.Clean(x.Validation.Query!)))
            .Where(x => !x.Clean.IsEmpty).ToList();
        var planned = queries.Count * s.Runs;
        if (s.MaxLlmCalls is { } max && planned > max)
            throw new InvalidOperationException($"Interpretation stability plans {planned} LLM calls ({queries.Count} queries × {s.Runs} runs), over the budget of {max}. "
                                                + "Use --limit to sample fewer queries or raise --max-llm-calls explicitly.");

        var rows = new List<JsonObject>();
        var perQuery = new List<IReadOnlyList<InterpretationOutcome>>();
        var pacer = new LlmPacer(s.LlmCallsPerMinute ?? EvalRunner.PacedRate(host));
        foreach (var (c, clean) in queries)
        {
            var runs = new List<InterpretationOutcome>();
            for (var i = 0; i < s.Runs; i++)
            {
                await pacer.WaitAsync(1, ct);
                await using var scope = host.Services.CreateAsyncScope();
                runs.Add(await scope.ServiceProvider.GetRequiredService<InterpretationService>().InterpretAsync(clean, ct));
            }
            perQuery.Add(runs);
            rows.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["query"] = c.Query,
                ["identical_title_author"] = AllSame(runs.Select(r => TitleAuthor(r.Hypothesis))),
                ["runs"] = new JsonArray([.. runs.Select(r => JsonSerializer.SerializeToNode(new
                {
                    r.Hypothesis, r.Source, r.Degraded, status = r.Call?.Status, cache_hit = r.Call?.CacheHit, model = r.Call?.Model,
                }, EvalJson.Options))]),
            });
        }

        var md = new StringBuilder();
        Header(md, "Interpretation stability", host, dataset, command, s);
        var n = perQuery.Count;
        var calls = perQuery.SelectMany(r => r).Where(r => r.Call is not null).ToList();
        md.AppendLine($"Queries: {n} (validated, non-empty after cleaning) × {s.Runs} runs = {n * s.Runs} interpreter calls; "
                      + $"cache hits observed: {calls.Count(r => r.Call!.CacheHit)} (must be 0 for a real measurement); "
                      + $"non-ok calls: {calls.Count(r => !r.Call!.Succeeded)}; runs without a hypothesis: {perQuery.SelectMany(r => r).Count(r => r.Hypothesis is null)}.");
        md.AppendLine();
        md.AppendLine("| Agreement across all runs (share of queries) | Value |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| **Normalized title + author identical** | **{new Ratio(perQuery.Count(r => AllSame(r.Select(x => TitleAuthor(x.Hypothesis)))), n)}** |");
        foreach (var (field, get) in Fields)
            md.AppendLine($"| {field} | {new Ratio(perQuery.Count(r => AllSame(r.Select(x => get(x.Hypothesis)))), n)} |");
        md.AppendLine($"| Whole hypothesis identical | {new Ratio(perQuery.Count(r => AllSame(r.Select(x => Whole(x.Hypothesis)))), n)} |");
        md.AppendLine($"| Source identical (llm vs heuristic) | {new Ratio(perQuery.Count(r => AllSame(r.Select(x => x.Source.ToString()))), n)} |");
        md.AppendLine();
        var unstable = perQuery.Select((r, i) => (Runs: r, Case: queries[i].Case)).Where(x => !AllSame(x.Runs.Select(y => TitleAuthor(y.Hypothesis)))).ToList();
        if (unstable.Count > 0)
        {
            md.AppendLine("| Unstable query | Distinct title / author answers |");
            md.AppendLine("|---|---|");
            foreach (var (runs, c) in unstable)
                md.AppendLine($"| {Fmt.Cell(c.Query)} | {Fmt.Cell(string.Join(" · ", runs.Select(r => TitleAuthor(r.Hypothesis)).Distinct()))} |");
            md.AppendLine();
        }
        md.AppendLine("Normalization: the pipeline's TextFolding.Tokens (folded, lower-case, punctuation dropped); characters/keywords compared as sets; a failed call counts as the value ∅.");
        var metrics = new JsonObject
        {
            ["kind"] = "interpret", ["dataset"] = $"{dataset.Name} v{dataset.Version}", ["held_out"] = dataset.HeldOut,
            ["queries"] = n, ["runs"] = s.Runs, ["provider"] = host.Options.Provider,
            ["models"] = new JsonArray([.. calls.Select(c => c.Call!.Model).Distinct().Select(m => (JsonNode)m)]),
            ["code_fingerprint"] = CodeFingerprint.OfSource().ToString(), ["binary_fingerprint"] = CodeFingerprint.OfBinaries().ToString(),
            ["started"] = DateTimeOffset.Now.ToString("O"),
            ["cache_hits"] = calls.Count(r => r.Call!.CacheHit), ["non_ok_calls"] = calls.Count(r => !r.Call!.Succeeded),
            ["title_author_identical"] = Rate(perQuery.Count(r => AllSame(r.Select(x => TitleAuthor(x.Hypothesis)))), n),
            ["whole_hypothesis_identical"] = Rate(perQuery.Count(r => AllSame(r.Select(x => Whole(x.Hypothesis)))), n),
        };
        Write(outDir, "interpret", md, rows, metrics);
        return md.ToString();
    }

    public static async Task<string> RetrievalAsync(EvalHost host, Dataset dataset, Settings s, string outDir, string command, CancellationToken ct)
    {
        var runner = new EvalRunner(host, new RunSettings
        {
            Concurrency = s.Concurrency, MaxLlmCalls = s.MaxLlmCalls, MaxCatalogRequests = s.MaxCatalogRequests,
            LlmCallsPerMinute = s.LlmCallsPerMinute,
        });
        var perQuery = new List<(EvalCase Case, IReadOnlyList<RowOutcome> Runs)>();
        var rows = new List<JsonObject>();
        foreach (var c in dataset.Cases)
        {
            var runs = new List<RowOutcome>();
            for (var i = 0; i < s.Runs; i++) runs.Add(await runner.RunCaseAsync(c, dataset.HeldOut, ct));
            if (runs.All(r => r.Row.Status == Outcomes.Rejected)) continue;
            perQuery.Add((c, runs));
            rows.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["query"] = c.Query,
                ["runs"] = new JsonArray([.. runs.Select(r => (JsonNode)new JsonObject
                {
                    ["status"] = r.Row.Status,
                    ["top5"] = new JsonArray([.. Top(r, 5).Select(x => (JsonNode)x)]),
                    ["pool"] = Pool(r).Count,
                    ["catalog_calls"] = r.Row.Meta.CatalogCalls.Count,
                    ["cache_hits"] = r.Row.Meta.CatalogCalls.Count(x => x.CacheHit),
                    ["error"] = r.Row.ErrorCode,
                })]),
            });
        }

        var md = new StringBuilder();
        Header(md, "Retrieval variability", host, dataset, command, s);
        var n = perQuery.Count;
        var all = perQuery.SelectMany(q => q.Runs).ToList();
        var skipped = all.Count(r => r.Row.Status == Outcomes.Skipped);
        md.AppendLine($"Queries: {n} × {s.Runs} runs; errors: {all.Count(r => r.Row.Status == Outcomes.Error)}; skipped (budget): {skipped}; "
                      + $"catalog cache hits observed: {all.Sum(r => r.Row.Meta.CatalogCalls.Count(c => c.CacheHit))} (must be 0 for a real measurement).");
        md.AppendLine();
        var complete = perQuery.Where(q => q.Runs.All(r => r.Outcome is not null)).ToList();
        md.AppendLine($"Over the {complete.Count} queries where every run completed:");
        md.AppendLine();
        md.AppendLine("| Stability across all runs (share of queries) | Value |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| **Top-1 identical** | **{new Ratio(complete.Count(q => AllSame(q.Runs.Select(r => Top(r, 1).FirstOrDefault() ?? "∅"))), complete.Count)}** |");
        md.AppendLine($"| Top-5 set identical | {new Ratio(complete.Count(q => AllSame(q.Runs.Select(r => string.Join(",", Top(r, 5).Order(StringComparer.Ordinal))))), complete.Count)} |");
        md.AppendLine($"| Top-5 order identical | {new Ratio(complete.Count(q => AllSame(q.Runs.Select(r => string.Join(",", Top(r, 5))))), complete.Count)} |");
        md.AppendLine($"| Status identical | {new Ratio(complete.Count(q => AllSame(q.Runs.Select(r => r.Row.Status))), complete.Count)} |");
        var jaccards = complete.Select(q => MeanPairwiseJaccard(q.Runs.Select(Pool).ToList())).ToList();
        md.AppendLine($"| Ranked-pool Jaccard (mean of per-query mean pairwise) | {Fmt.Num(Fmt.Mean(jaccards), "0.000")} (min {Fmt.Num(jaccards.Count == 0 ? null : jaccards.Min(), "0.000")}) |");
        md.AppendLine();
        var unstable = complete.Where(q => !AllSame(q.Runs.Select(r => Top(r, 1).FirstOrDefault() ?? "∅"))).ToList();
        if (unstable.Count > 0)
        {
            md.AppendLine("| Unstable query | Top-1 per run |");
            md.AppendLine("|---|---|");
            foreach (var (c, runs) in unstable)
                md.AppendLine($"| {Fmt.Cell(c.Query)} | {string.Join(" · ", runs.Select(r => Top(r, 1).FirstOrDefault() ?? "∅"))} |");
            md.AppendLine();
        }
        md.AppendLine("Pool = the full ranked list after dedupe (SearchOutcome.Ranking), compared as sets of work ids.");
        var metrics = new JsonObject
        {
            ["kind"] = "retrieval", ["dataset"] = $"{dataset.Name} v{dataset.Version}", ["held_out"] = dataset.HeldOut,
            ["queries"] = complete.Count, ["runs"] = s.Runs, ["catalog"] = host.Options.Catalog.ToString().ToLowerInvariant(),
            ["code_fingerprint"] = CodeFingerprint.OfSource().ToString(), ["binary_fingerprint"] = CodeFingerprint.OfBinaries().ToString(),
            ["started"] = DateTimeOffset.Now.ToString("O"),
            ["cache_hits"] = all.Sum(r => r.Row.Meta.CatalogCalls.Count(c => c.CacheHit)),
            ["top1_identical"] = Rate(complete.Count(q => AllSame(q.Runs.Select(r => Top(r, 1).FirstOrDefault() ?? "∅"))), complete.Count),
            ["top5_set_identical"] = Rate(complete.Count(q => AllSame(q.Runs.Select(r => string.Join(",", Top(r, 5).Order(StringComparer.Ordinal))))), complete.Count),
            ["status_identical"] = Rate(complete.Count(q => AllSame(q.Runs.Select(r => r.Row.Status))), complete.Count),
            ["pool_jaccard_mean"] = Fmt.Mean(jaccards),
        };
        Write(outDir, "retrieval", md, rows, metrics);
        return md.ToString();
    }

    private static readonly (string Name, Func<QueryHypothesis?, string> Get)[] Fields =
    [
        ("title", h => N(h?.Title)),
        ("author", h => N(h?.Author)),
        ("english_title", h => N(h?.EnglishTitle)),
        ("original_title", h => N(h?.OriginalTitle)),
        ("characters (set)", h => Set(h?.Characters)),
        ("keywords (set)", h => Set(h?.Keywords)),
    ];

    private static string TitleAuthor(QueryHypothesis? h) => h is null ? "∅" : $"{N(h.Title)} / {N(h.Author)}";

    private static string Whole(QueryHypothesis? h) => h is null ? "∅" : string.Join(" | ", Fields.Select(f => f.Get(h)));

    private static string N(string? s) => s is null ? "∅" : string.Join(' ', TextFolding.Tokens(s));

    private static string Set(IReadOnlyList<string>? xs) => xs is null ? "∅" : string.Join(',', xs.Select(N).Distinct().Order(StringComparer.Ordinal));

    private static bool AllSame(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Count() <= 1;

    private static IReadOnlyList<string> Top(RowOutcome r, int k) => r.Row.Top5.Take(k).Select(x => x.WorkId).ToList();

    private static IReadOnlySet<string> Pool(RowOutcome r) =>
        r.Outcome?.Ranking.Ranked.Select(w => w.Work.WorkId).ToHashSet(StringComparer.Ordinal) ?? [];

    public static double MeanPairwiseJaccard(IReadOnlyList<IReadOnlySet<string>> sets)
    {
        if (sets.Count < 2) return 1;
        var total = 0.0;
        var pairs = 0;
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
            {
                var union = sets[i].Union(sets[j]).Count();
                total += union == 0 ? 1 : (double)sets[i].Intersect(sets[j]).Count() / union;
                pairs++;
            }
        return total / pairs;
    }

    private static void Header(StringBuilder md, string title, EvalHost host, Dataset d, string command, Settings s)
    {
        md.AppendLine($"# {title}: {d.Name} v{d.Version}");
        md.AppendLine();
        if (d.HeldOut) md.AppendLine("> **HELD-OUT RESULTS.** Sealed set, final evaluation only.\n");
        if (host.Options.Catalog == CatalogMode.Mirror) md.AppendLine("> Synthetic, local mirror (bm25, OR fallback), not Open Library's ranking.\n");
        md.AppendLine($"- Date: {RunInfo.Stamp(DateTimeOffset.Now)}");
        md.AppendLine($"- Code fingerprint: `{CodeFingerprint.OfSource()}` · pipeline binaries `{CodeFingerprint.OfBinaries()}` · eval `{CodeFingerprint.OfEvalTool()}`");
        md.AppendLine($"- Dataset: `{RepoPaths.Relative(d.Path)}` sha256:{d.Sha256}, {d.Cases.Count} cases");
        md.AppendLine($"- Provider: {host.Options.Provider}{(host.Options.Model is null ? "" : $" / {host.Options.Model}")} · catalog: {host.Options.Catalog.ToString().ToLowerInvariant()} · "
                      + $"judge: {(host.Options.Judge ? "on" : "off")} · caches: bypassed · runs: {s.Runs}");
        md.AppendLine($"- Command: `{Fmt.Cell(command)}`");
        md.AppendLine();
    }

    private static JsonObject Rate(int hits, int total) => new() { ["hits"] = hits, ["total"] = total, ["value"] = total == 0 ? null : (double)hits / total };

    private static void Write(string outDir, string kind, StringBuilder md, IEnumerable<JsonObject> rows, JsonObject metrics)
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, $"stability-{kind}.json"), metrics.ToJsonString(EvalJson.Indented) + "\n");
        File.WriteAllText(Path.Combine(outDir, $"stability-{kind}.md"), md.ToString());
        File.WriteAllLines(Path.Combine(outDir, $"stability-{kind}.jsonl"), rows.Select(r => r.ToJsonString(EvalJson.Options)));
    }
}
