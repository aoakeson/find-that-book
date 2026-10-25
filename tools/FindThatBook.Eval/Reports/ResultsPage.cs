using System.Text;
using System.Text.Json.Nodes;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Eval.Reports;

/// <summary>
/// The headline part of eval/RESULTS.md: one table per dataset (final-build runs only), then providers and cost,
/// explanations and the judge. Every row names its dataset + version, sample (in-sample / held-out), catalog,
/// provider/model and build, and links to the run folder. All numbers are read from the runs' own files.
/// </summary>
public static class ResultsPage
{
    public sealed record Run(string Folder, JsonObject Summary, IReadOnlyList<ResultRow> Rows)
    {
        public string Link => $"[{Name}](results/{Name}/summary.md)";
        public string Name => Path.GetRelativePath(RepoPaths.Results, Folder).Replace('\\', '/');
        public JsonNode Dataset => Summary["dataset"]!;
        public string DatasetName => $"{Dataset["name"]} v{Dataset["version"]}";
        public bool HeldOut => Dataset["held_out"]?.GetValue<bool>() == true;
        public string Sample => HeldOut ? "**HELD-OUT**"
            : (Dataset["tuning"]?.GetValue<string>() ?? "") is var t && (t.StartsWith("in_sample", StringComparison.Ordinal) || t.StartsWith("dev", StringComparison.Ordinal))
                ? "in-sample" : "not tuned on";
        public string Catalog => Summary["catalog"]?.GetValue<string>() ?? "n/a";
        public string Build => $"`{Short(Summary["code_fingerprint"])}` / bin `{Short(Summary["binary_fingerprint"])}`";
        public string Started => Summary["started"]?.GetValue<string>() ?? "";

        /// <summary>"gemini-3.5-flash-lite interpret + explain", "gpt-4.1-mini interpretations replayed from …", "LLM off".</summary>
        public string Arm
        {
            get
            {
                var models = (Summary["observed_models"] as JsonArray)?.Select(m => m?.GetValue<string>() ?? "").ToList() ?? [];
                var interpret = models.FirstOrDefault(m => m.StartsWith("interpret:", StringComparison.Ordinal))?[10..].Trim();
                var explain = Summary["explain"]?.GetValue<string>() == "llm" && models.Any(m => m.StartsWith("explain:", StringComparison.Ordinal));
                string what;
                if (Summary["hypotheses"]?.GetValue<string>() is { } h)
                {
                    var source = Summary["hypotheses_recorded_on"]?.GetValue<string>() is { } rec && rec.Contains("(interpret: ", StringComparison.Ordinal)
                        ? rec[(rec.IndexOf("(interpret: ", StringComparison.Ordinal) + 12)..].Split(')')[0] : "recorded";
                    what = $"{Model(source)} interpretations replayed from `{Path.GetFileName(Path.GetDirectoryName(h) is { } d && Path.GetFileName(d) == "raw" ? Path.GetDirectoryName(d) : Path.GetDirectoryName(h))}`";
                }
                else what = interpret is null || interpret.StartsWith("replay", StringComparison.Ordinal) || interpret.StartsWith("none", StringComparison.Ordinal)
                    ? "LLM off" : $"{Model(interpret)} interpret";
                if (explain) what += " + explain";
                if (Summary["judge"]?.GetValue<bool>() == true) what += " + Decisions judge";
                return what;
            }
        }
    }

    private static string Model(string providerModel) => providerModel.Contains('/') ? providerModel[(providerModel.IndexOf('/') + 1)..] : providerModel;

    public static void Write(StringBuilder md, IReadOnlyList<Run> final, IReadOnlyList<Run> all)
    {
        md.AppendLine("## Headline results (final build, live Open Library)");
        md.AppendLine();
        var order = new[] { "golden", "hard", "HELD-OUT", "topic" };
        var groups = final.GroupBy(r => r.DatasetName)
            .OrderBy(g => Array.FindIndex(order, o => g.Key.Contains(o, StringComparison.OrdinalIgnoreCase)) is var i && i < 0 ? 99 : i)
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var runs = g.OrderBy(r => r.Started, StringComparer.Ordinal).ToList();
            var first = runs[0];
            md.AppendLine($"### {g.Key} · {first.Sample} · {first.Rows.Count} rows");
            md.AppendLine();
            if (first.HeldOut) md.AppendLine("> Sealed set, run once on the final build. Nothing was tuned on these rows.\n");
            md.AppendLine("| Arm | top-1 | top-5 | status | `match` @ precision | coverage | false / severe matches | p50 / p95 ms | catalog | build | run |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in runs)
            {
                var q = SummaryWriter.Quality(r.Rows);
                var searched = r.Rows.Where(x => x.Meta.ElapsedMs is not null).Select(x => (double)x.Meta.ElapsedMs!.Value).ToList();
                var providerErrors = r.Rows.Count(x => EvalRunner.HasProviderError(x.Meta));
                var arm = r.Arm + (providerErrors > 0 ? $" ({providerErrors} {(providerErrors == 1 ? "row" : "rows")} hit provider errors)" : "");
                md.AppendLine($"| {Fmt.Cell(arm)} | {q.Top1} | {q.Top5} | {q.Status} | {q.MatchPrecision} | {q.Coverage} | {q.FalseMatches} / {q.SevereMatches} | "
                              + $"{Fmt.Num(Fmt.Percentile(searched, 50), "0")} / {Fmt.Num(Fmt.Percentile(searched, 95), "0")} | {r.Catalog} | {r.Build} | {r.Link} |");
            }
            md.AppendLine();
            if (runs.Any(r => r.Rows.Any(x => x.Category == "topic"))) TopicSplit(md, runs);
        }
        Providers(md, final);
        Explanations(md, all);
        Judge(md, final);
    }

    private static void TopicSplit(StringBuilder md, IReadOnlyList<Run> runs)
    {
        md.AppendLine("Topic requests and controls, separately (topic rows pass on status `ambiguous` with reason `topic_search` "
                      + "and ≥ 3 of the top 5 on topic by subject; controls are real titles scored like golden rows):");
        md.AppendLine();
        md.AppendLine("| Arm | topic rows: ≥ 3/5 on topic | topic rows: status | controls: top-1 | controls: `match` @ P | topic + author, no hits: status | run |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in runs)
        {
            QualityStats Of(string cat) => SummaryWriter.Quality(r.Rows.Where(x => x.Category == cat));
            var t = Of("topic");
            var c = Of("topic_control");
            var a = Of("topic_author");
            var spoiled = r.Rows.Count(x => EvalRunner.HasProviderError(x.Meta));
            md.AppendLine($"| {Fmt.Cell(r.Arm + (spoiled > 0 ? $" ({spoiled} {(spoiled == 1 ? "row" : "rows")} hit provider errors and ran LLM-off)" : ""))} | {t.Top5} | {t.Status} | {c.Top1} | {c.MatchPrecision} | {a.Status} | {r.Link} |");
        }
        md.AppendLine();
    }

    private static void Providers(StringBuilder md, IReadOnlyList<Run> final)
    {
        var live = final.Where(r => r.Rows.SelectMany(x => x.Meta.LlmCalls).Any(l => !l.CacheHit && l.Provider is not ("replay" or "none" or "eval"))
                                    || r.Rows.Any(x => x.Meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) })).ToList();
        if (live.Count == 0) return;
        md.AppendLine("### Providers, latency and cost (runs that called an LLM)");
        md.AppendLine();
        md.AppendLine("| Dataset · arm | provider / model | operation | paid calls | p50 / p95 ms | tokens in / out per call | est. cost per 1,000 searches | build | run |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in live.OrderBy(r => r.Started, StringComparer.Ordinal))
        {
            var searched = r.Rows.Where(x => x.Meta.ElapsedMs is not null).ToList();
            var cost = searched.Where(x => x.Meta.EstCostUsd is not null).ToList();
            var perK = cost.Count == 0 ? "n/a" : Fmt.Usd(cost.Sum(x => x.Meta.EstCostUsd!.Value) / searched.Count * 1000);
            foreach (var op in searched.SelectMany(x => x.Meta.LlmCalls).Where(l => !l.CacheHit && l.Provider is not ("replay" or "none" or "eval"))
                         .GroupBy(l => (l.Operation, l.Provider, l.Model)).OrderBy(g => g.Key.Operation, StringComparer.Ordinal))
            {
                var paid = op.Where(l => l.Status is not (LlmCallStatus.NotConfigured or LlmCallStatus.BudgetExhausted)).ToList();
                var ok = paid.Where(l => l.Succeeded).ToList();
                md.AppendLine($"| {r.DatasetName} · {Fmt.Cell(r.Arm)} | {op.Key.Provider} / {op.Key.Model} | {op.Key.Operation} | {paid.Count} | "
                              + $"{Fmt.Num(Fmt.Percentile(ok.Select(l => (double)l.ElapsedMs), 50), "0")} / {Fmt.Num(Fmt.Percentile(ok.Select(l => (double)l.ElapsedMs), 95), "0")} | "
                              + $"{Fmt.Num(Fmt.Mean(ok.Where(l => l.InputTokens is not null).Select(l => (double)l.InputTokens!.Value)), "0")} / "
                              + $"{Fmt.Num(Fmt.Mean(ok.Where(l => l.OutputTokens is not null).Select(l => (double)l.OutputTokens!.Value)), "0")} | {perK} | {r.Build} | {r.Link} |");
            }
            var judged = searched.Where(x => x.Meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) }).ToList();
            if (judged.Count > 0)
                md.AppendLine($"| {r.DatasetName} · {Fmt.Cell(r.Arm)} | openai / {judged[0].Meta.Judge!.Model} | judge (Decisions) | {judged.Count} | "
                              + $"{Fmt.Num(Fmt.Percentile(judged.Select(x => (double)x.Meta.Judge!.ElapsedMs), 50), "0")} / {Fmt.Num(Fmt.Percentile(judged.Select(x => (double)x.Meta.Judge!.ElapsedMs), 95), "0")} | "
                              + $"{Fmt.Num(Fmt.Mean(judged.Where(x => x.Meta.Judge!.InputTokens is not null).Select(x => (double)x.Meta.Judge!.InputTokens!.Value)), "0")} / n/a | {perK} | {r.Build} | {r.Link} |");
        }
        md.AppendLine();
        md.AppendLine("Cost = list-price estimate (IAiPricing) of the paid calls a run made, per 1,000 searches; cache hits and 429s cost nothing; "
                      + "a call without reported usage makes it a lower bound.");
        md.AppendLine();
    }

    private static void Explanations(StringBuilder md, IReadOnlyList<Run> all)
    {
        var runs = all.Where(r => r.Summary["explain"]?.GetValue<string>() == "llm" && r.Summary["stopped"] is null
                                  && r.Rows.SelectMany(x => x.Meta.LlmCalls).Any(l => l.Operation == "explain" && l.Succeeded)).ToList();
        if (runs.Count == 0) return;
        md.AppendLine("### Explanations (runs with the explain LLM on; any build: explanations do not depend on the interpret prompt)");
        md.AppendLine();
        md.AppendLine("| Dataset · arm | LLM sentences | template sentences | grounding rejections | rows with explanation_fallback | build | run |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in runs.OrderBy(r => r.Started, StringComparer.Ordinal))
        {
            var s = r.Rows.Where(x => x.Meta.ElapsedMs is not null).ToList();
            md.AppendLine($"| {r.DatasetName} · {Fmt.Cell(r.Arm)} | {s.Sum(x => x.Meta.ExplanationsLlm)} | {s.Sum(x => x.Meta.ExplanationsTemplate)} | "
                          + $"{s.Sum(x => x.Meta.TemplateAfterLlmOk)} | {s.Count(x => x.Meta.Degraded.Contains(FindThatBook.Core.Model.DegradedReason.ExplanationFallback))} | {r.Build} | {r.Link} |");
        }
        md.AppendLine();
    }

    private static void Judge(StringBuilder md, IReadOnlyList<Run> final)
    {
        var judged = final.Where(r => r.Summary["judge"]?.GetValue<bool>() == true).ToList();
        if (judged.Count == 0) return;
        md.AppendLine("### Decisions judge (status only; it never reorders)");
        md.AppendLine();
        md.AppendLine("| Dataset | interpretations | agreed / decided | judge pick right · code #1 right (rows with a pick) | status · `match` @ P · coverage, judge off → on | run |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in judged)
        {
            var rows = r.Rows.Where(x => x.Meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) }).ToList();
            var decided = rows.Count(x => x.Meta.Judge!.Status is JudgeStatus.Agreed or JudgeStatus.Disagreed or JudgeStatus.ChoseNone);
            var agreed = rows.Count(x => x.Meta.Judge!.Status == JudgeStatus.Agreed);
            var picked = rows.Where(x => x.Meta.Judge!.Pick is not null && x.Expected.Top1.Count > 0).ToList();
            // The same dataset and interpretations without the judge: the run the hypotheses were recorded in.
            var offFolder = r.Summary["hypotheses"]?.GetValue<string>() is { } h ? Path.Combine(RepoPaths.Root, h.StartsWith("research archive", StringComparison.Ordinal) ? "" : h) : null;
            var off = offFolder is null ? null : FindRunFolder(offFolder);
            var on = SummaryWriter.Quality(r.Rows);
            static string Line(QualityStats q) => $"{Fmt.Rate(q.Status.Value)} · {q.MatchPrecision.Total} @ {Fmt.Rate(q.MatchPrecision.Value)} · {Fmt.Rate(q.Coverage.Value)}";
            var delta = off is null ? "n/a" : $"{Line(SummaryWriter.Quality(Commands.LoadRows(off)))} → {Line(on)}";
            md.AppendLine($"| {r.DatasetName} ({r.Sample}) | {Fmt.Cell(r.Arm)} | {new Ratio(agreed, decided)} | "
                          + $"{picked.Count(x => x.Expected.Top1.Contains(x.Meta.Judge!.Pick!))} · {picked.Count(x => x.Pass.Top1 == true)} (of {picked.Count}) | {delta} | {r.Link} |");
        }
        md.AppendLine();
    }

    private static string? FindRunFolder(string resultsFile)
    {
        var dir = Path.GetDirectoryName(resultsFile);
        if (dir is not null && Path.GetFileName(dir) == "raw") dir = Path.GetDirectoryName(dir);
        return dir is not null && File.Exists(Path.Combine(dir, "summary.json")) ? dir : null;
    }

    private static string Short(JsonNode? fingerprint) =>
        fingerprint?.GetValue<string>() is { } f ? f.Split(' ')[0].Replace("sha256:", "")[..8] : "n/a";
}
