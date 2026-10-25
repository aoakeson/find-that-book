using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Eval.Reports;

/// <summary>
/// Writes a result folder: results.jsonl (one row per query), summary.md (the human report), summary.json (the same
/// numbers for compare/tools) and raw/outcomes.jsonl (full pipeline outcomes; gitignored). Only measured numbers are
/// printed; anything not measured in this run prints "n/a".
/// </summary>
public static class SummaryWriter
{
    /// <summary>Above this many rows results.jsonl goes to raw/ (gitignored; ~3 KB per row) and full outcomes are skipped.</summary>
    public const int CommittedRowLimit = 300;
    public const int RawOutcomeRowLimit = 200;

    public static string ResultsPath(string folder, int rows) =>
        rows > CommittedRowLimit ? Path.Combine(folder, "raw", "results.jsonl") : Path.Combine(folder, "results.jsonl");

    /// <summary>results.jsonl in the folder, or in its raw/ subfolder for large runs.</summary>
    public static string? FindResults(string folder) =>
        new[] { Path.Combine(folder, "results.jsonl"), Path.Combine(folder, "raw", "results.jsonl") }.FirstOrDefault(File.Exists);

    public static void Write(string folder, RunInfo info, IReadOnlyList<RowOutcome> outcomes)
    {
        Directory.CreateDirectory(folder);
        var rows = outcomes.Select(o => o.Row).ToList();
        var results = ResultsPath(folder, rows.Count);
        Directory.CreateDirectory(Path.GetDirectoryName(results)!);
        File.WriteAllLines(results, rows.Select(r => JsonSerializer.Serialize(r, EvalJson.Options)));
        if (rows.Count <= RawOutcomeRowLimit) WriteRaw(Path.Combine(folder, "raw"), outcomes);
        File.WriteAllText(Path.Combine(folder, "summary.json"), Json(info, rows).ToJsonString(EvalJson.Indented) + "\n");
        File.WriteAllText(Path.Combine(folder, "summary.md"), Markdown(info, rows));
    }

    public static QualityStats Quality(IEnumerable<ResultRow> rows) =>
        QualityStats.Compute(rows.Select(r => (r.Status, r.Pass.ToScore(r.Status))));

    public static IReadOnlyDictionary<string, QualityStats> ByCategory(IEnumerable<ResultRow> rows) =>
        rows.GroupBy(r => r.Category).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Quality(g), StringComparer.Ordinal);

    private static JsonObject Json(RunInfo info, IReadOnlyList<ResultRow> rows)
    {
        var run = rows.Where(r => r.Status != Outcomes.Skipped).ToList();
        return new JsonObject
        {
            ["label"] = info.Label,
            ["command"] = info.Command,
            ["started"] = info.Started.ToString("O"),
            ["wall_seconds"] = Math.Round(info.Wall.TotalSeconds, 1),
            ["dataset"] = new JsonObject
            {
                ["name"] = info.Dataset.Name, ["version"] = info.Dataset.Version, ["path"] = RepoPaths.Relative(info.Dataset.Path),
                ["sha256"] = info.Dataset.Sha256, ["held_out"] = info.Dataset.HeldOut, ["tuning"] = info.Dataset.Tuning,
                ["cases"] = info.Dataset.Cases.Count,
            },
            ["code_fingerprint"] = info.Source.ToString(),
            ["eval_fingerprint"] = info.Tool.ToString(),
            ["binary_fingerprint"] = CodeFingerprint.OfBinaries().ToString(),
            ["catalog"] = info.Host.Catalog.ToString().ToLowerInvariant(),
            ["provider"] = info.Host.Provider,
            ["model"] = info.Host.Model,
            ["observed_models"] = new JsonArray([.. ObservedModels(run).Select(m => (JsonNode)m)]),
            ["judge"] = info.Host.Judge,
            ["hypotheses"] = info.Hypotheses is null ? null : RepoPaths.Relative(info.Hypotheses),
            ["hypotheses_recorded_on"] = info.Hypotheses is null ? null : RecordedOn(info.Hypotheses) is { Length: > 0 } rec ? rec.TrimStart(';', ' ') : null,
            ["explain"] = info.Host.Explain,
            ["ranker"] = info.RankerArm,
            ["catalog_cache"] = info.Host.CatalogCache is null ? null : RepoPaths.Relative(info.Host.CatalogCache),
            ["label_note"] = info.Dataset.Label,
            ["held_out"] = info.Dataset.HeldOut,
            ["bypass_caches"] = info.Host.BypassCaches,
            ["stopped"] = info.StoppedReason,
            ["counts"] = JsonSerializer.SerializeToNode(Counts(rows), EvalJson.Options),
            ["ops"] = Ops(run),
            ["overall"] = JsonSerializer.SerializeToNode(Quality(rows), EvalJson.Options),
            ["categories"] = JsonSerializer.SerializeToNode(ByCategory(rows), EvalJson.Options),
            ["excluding_replay_misses"] = info.Host.Catalog == CatalogMode.Replay
                ? JsonSerializer.SerializeToNode(Quality(rows.Where(r => r.Meta.Replay is not { Misses.Count: > 0 })), EvalJson.Options)
                : null,
        };
    }

    /// <summary>Operational numbers for the index: latency, Open Library calls, LLM cost, degraded split by cause.</summary>
    public static JsonObject Ops(IReadOnlyList<ResultRow> run)
    {
        var searched = run.Where(r => r.Meta.ElapsedMs is not null).ToList();
        var priced = searched.Where(r => r.Meta.EstCostUsd is not null).ToList();
        var settingOnly = searched.Count(r => r.Meta.Degraded.Count > 0 && r.Meta.Degraded.All(d => d is DegradedReason.AiNotConfigured or DegradedReason.ExplanationFallback));
        var shownLlm = searched.Sum(r => r.Meta.ExplanationsLlm);
        var shownTemplate = searched.Sum(r => r.Meta.ExplanationsTemplate);
        return new JsonObject
        {
            ["searched"] = searched.Count,
            ["latency_p50_ms"] = Fmt.Percentile(searched.Select(r => (double)r.Meta.ElapsedMs!.Value), 50),
            ["latency_p95_ms"] = Fmt.Percentile(searched.Select(r => (double)r.Meta.ElapsedMs!.Value), 95),
            ["ol_calls_per_query"] = Fmt.Mean(searched.Select(r => (double)r.Meta.CatalogCalls.Count)),
            ["ol_attempts_per_query"] = Fmt.Mean(searched.Select(r => (double)r.Meta.CatalogCalls.Sum(c => c.Attempts))),
            ["paid_llm_calls"] = searched.Sum(r => EvalRunner.PaidLlmCalls(r.Meta)),
            ["input_tokens"] = searched.SelectMany(r => r.Meta.LlmCalls).Where(l => !l.CacheHit).Sum(l => l.InputTokens ?? 0),
            ["output_tokens"] = searched.SelectMany(r => r.Meta.LlmCalls).Where(l => !l.CacheHit).Sum(l => l.OutputTokens ?? 0),
            ["cost_per_1k_usd"] = priced.Count == 0 ? null : priced.Sum(r => r.Meta.EstCostUsd!.Value) / searched.Count * 1000,
            ["degraded_rows"] = searched.Count(r => r.Meta.Degraded.Count > 0),
            ["degraded_by_settings_rows"] = settingOnly,
            ["provider_error_rows"] = searched.Count(r => EvalRunner.HasProviderError(r.Meta)),
            ["explanations_llm"] = shownLlm,
            ["explanations_template"] = shownTemplate,
            ["grounding_rejections"] = searched.Sum(r => r.Meta.TemplateAfterLlmOk),
        };
    }

    private sealed record RowCounts(int Cases, int Run, int Skipped, int Rejected, int Errors, int ReplayMissRows, int InSample);

    private static RowCounts Counts(IReadOnlyList<ResultRow> rows) => new(
        rows.Count,
        rows.Count(r => r.Status != Outcomes.Skipped),
        rows.Count(r => r.Status == Outcomes.Skipped),
        rows.Count(r => r.Status == Outcomes.Rejected),
        rows.Count(r => r.Status == Outcomes.Error),
        rows.Count(r => r.Meta.Replay is { Misses.Count: > 0 }),
        rows.Count(r => r.InSample));

    private static IEnumerable<string> ObservedModels(IEnumerable<ResultRow> rows) =>
        rows.SelectMany(r => r.Meta.LlmCalls.Where(l => l.Status != LlmCallStatus.NotConfigured).Select(l => $"{l.Operation}: {l.Provider}/{l.Model}")
                .Concat(r.Meta.Judge?.Model is { } m ? [$"judge: {m}"] : []))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    public static string Markdown(RunInfo info, IReadOnlyList<ResultRow> rows)
    {
        var md = new StringBuilder();
        var run = rows.Where(r => r.Status != Outcomes.Skipped).ToList();
        var counts = Counts(rows);
        var d = info.Dataset;

        md.AppendLine($"# Eval: {d.Name} v{d.Version} · {info.Label}");
        md.AppendLine();
        if (d.HeldOut) md.AppendLine("> **HELD-OUT RESULTS.** Sealed set, final evaluation only. Nothing may be tuned on these rows.\n");
        if (info.Host.Catalog == CatalogMode.Mirror)
            md.AppendLine("> **Synthetic, local mirror (bm25, OR fallback), not Open Library's ranking.** The mirror ignores `author_key` "
                          + "and `sort`, has no person index (`person:` calls are answered empty and counted) and no `editions.title` / "
                          + "`author_alternative_name`, so the numbers measure the cleaner, the retrieval ladder and the ranker on "
                          + "machine-generated noise, not headline quality.\n");
        if (d.Label is { } label && !(info.Host.Catalog == CatalogMode.Mirror && label.StartsWith("synthetic, local mirror", StringComparison.Ordinal)))
            md.AppendLine($"> {label}\n");
        if (info.StoppedReason is { } stopped) md.AppendLine($"> **Partial run:** {stopped}. {counts.Skipped} rows were not run and are not scored.\n");
        if (info.ResumedFrom is { } resumed)
            md.AppendLine($"> **Resumed:** {info.ResumedRows} rows (skipped or spoiled by provider errors) re-run; the other rows are kept from `{RepoPaths.Relative(resumed)}`.\n");

        md.AppendLine("| Run | |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Date | {RunInfo.Stamp(info.Started)} |");
        md.AppendLine($"| Code fingerprint (src/) | `{info.Source}` |");
        md.AppendLine($"| Pipeline binaries (Core + Infrastructure dlls loaded) | `{CodeFingerprint.OfBinaries()}` |");
        md.AppendLine($"| Eval tool fingerprint | `{info.Tool}` |");
        md.AppendLine($"| Dataset | {d.Name} v{d.Version} (`{RepoPaths.Relative(d.Path)}`, sha256:{d.Sha256}) |");
        md.AppendLine($"| Held out | {(d.HeldOut ? "**yes**" : "no")} |");
        md.AppendLine($"| Tuning | {Fmt.Cell(d.Tuning)} |");
        md.AppendLine($"| Catalog | {Fmt.Cell(info.CatalogDescription)} |");
        if (info.ReplaySources.Count > 0)
            md.AppendLine($"| Replay sources | {string.Join("<br>", info.ReplaySources.Select(s => $"`{Display(s.Folder)}` ({s.Files})"))} |");
        md.AppendLine($"| Provider (requested) | {info.Host.Provider}{(info.Host.Model is null ? "" : $" / {info.Host.Model}")} |");
        var models = ObservedModels(run).ToList();
        md.AppendLine($"| Models (observed in traces) | {(models.Count == 0 ? "none (no LLM call reached a provider)" : string.Join("<br>", models))} |");
        md.AppendLine($"| Interpretations | {(info.Hypotheses is { } hp ? $"replayed from `{Display(hp)}` ({info.HypothesisEntries} entries; {(info.Host.HypothesesFillLive ? $"queries not in it interpreted live: {run.Count(r => r.Meta.LlmCalls.Any(l => l.Operation == "interpret" && l.Provider != "replay"))}" : $"{run.Count(r => r.Meta.HypothesisMiss)} hypothesis_miss rows ran LLM-off")}){RecordedOn(hp)}" : info.Host.InterpretsWithLlm ? "live LLM" : "none (LLM off)")} |");
        md.AppendLine($"| Explanations | {(info.Host.Explain == "template" ? "template only (--explain template: the explain LLM is never called)" : info.Host.ExplainsWithLlm ? "live LLM" : "template (LLM off)")} |");
        md.AppendLine($"| Ranker | `{info.RankerArm}` |");
        md.AppendLine($"| Judge | {(info.Host.Judge ? "on" : "off")} |");
        if (info.Host.CatalogCache is { } cache)
            md.AppendLine($"| Catalog cache | `{RepoPaths.Relative(cache)}`: {info.CacheFills} requests fetched from the backend this run; {info.Unsupported} unsupported calls answered empty (person: on the mirror) |");
        md.AppendLine($"| Caches | {(info.Host.BypassCaches ? "bypassed (pass-through HybridCache)" : "as configured (cold at process start)")} |");
        md.AppendLine($"| Counts | {counts.Cases} cases · {counts.Run} run · {counts.Skipped} skipped · {counts.Rejected} rejected by validation · {counts.Errors} errors · {counts.InSample} in-sample |");
        md.AppendLine($"| Concurrency / limit | {info.Concurrency} / {info.ResultLimit}{(info.Host.Provider == "none" ? "" : $" · LLM paced to {(info.LlmPace == 0 ? "no limit" : $"{info.LlmPace} calls/min")}")} |");
        md.AppendLine($"| Wall time | {info.Wall.TotalSeconds:0.0} s |");
        md.AppendLine($"| Command | `{Fmt.Cell(info.Command)}` |");
        if (rows.Count > CommittedRowLimit)
            md.AppendLine($"| Per-row results | `raw/results.jsonl` ({rows.Count} rows; gitignored, kept locally) |");
        md.AppendLine();

        Headline(md, rows, info);
        Categories(md, rows);
        if (info.Host.Catalog == CatalogMode.Replay || info.Host.CatalogCache is not null) Replay(md, rows);
        Judge(md, run, info);
        Explanations(md, run);
        Degraded(md, run);
        Latency(md, run, info);
        OpenLibrary(md, run, info);
        Llm(md, run);
        Misses(md, rows);
        if (PrototypeReference.For(d.Name, d.Path) is { } reference)
        {
            md.AppendLine("## Prototype reference (not this build)");
            md.AppendLine();
            md.AppendLine(reference.TrimEnd());
            md.AppendLine();
        }
        md.AppendLine("Parse stability and retrieval variability are measured by the `stability` command (separate report).");
        return md.ToString();
    }

    /// <summary>"interpretations recorded on build X, ranking re-run on build Y": the source run's fingerprint, when the file is an eval run's results.</summary>
    private static string RecordedOn(string hypotheses)
    {
        var folder = Path.GetDirectoryName(hypotheses)!;
        if (Path.GetFileName(folder) == "raw") folder = Path.GetDirectoryName(folder)!;
        var summary = Path.Combine(folder, "summary.json");
        if (!File.Exists(summary) || JsonNode.Parse(File.ReadAllText(summary)) is not JsonObject j) return "";
        return $"; **interpretations recorded on build `{j["code_fingerprint"]}`** ({j["observed_models"]?.AsArray().Select(m => m?.GetValue<string>()).FirstOrDefault(m => m?.StartsWith("interpret:", StringComparison.Ordinal) == true) ?? "model n/a"}), "
               + $"ranking and status re-run on build `{CodeFingerprint.OfSource()}`";
    }

    /// <summary>A path as shown in reports: repo-relative, "research archive/…", or just its name (never a machine path).</summary>
    public static string Display(string path) => RepoPaths.Relative(path);

    private static void Headline(StringBuilder md, IReadOnlyList<ResultRow> rows, RunInfo info)
    {
        var q = Quality(rows);
        md.AppendLine("## Headline");
        md.AppendLine();
        md.AppendLine("| Metric | Value | Definition |");
        md.AppendLine("|---|---|---|");
        md.AppendLine($"| **Match precision** | **{q.MatchPrecision}** | `match` rows whose top-1 is right (and the row allows match) |");
        md.AppendLine($"| Top-1 | {q.Top1} | answerable rows, first shown candidate acceptable |");
        md.AppendLine($"| Top-5 | {q.Top5} | answerable rows, an acceptable candidate in the first five |");
        md.AppendLine($"| Status accuracy | {q.Status} | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |");
        md.AppendLine($"| Coverage (auto-accept) | {q.Coverage} | answerable rows returned as `match` |");
        md.AppendLine($"| False matches on no-answer rows | {q.FalseMatches} of {q.NoAnswerRows} | any `match` where no book is right |");
        md.AppendLine($"| Severe matches | {q.SevereMatches} | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |");
        md.AppendLine($"| Duplicate-record matches | {q.DupRecordMatches} | `match` on another record of the intended book (battle major) |");
        md.AppendLine($"| Errors | {q.Errors} | the search threw (catalog failure, timeout, crash) |");
        md.AppendLine();
        if (info.Host.Catalog == CatalogMode.Replay && rows.Any(r => r.Meta.Replay is { Misses.Count: > 0 }))
        {
            var clean = Quality(rows.Where(r => r.Meta.Replay is not { Misses.Count: > 0 }));
            md.AppendLine($"Excluding rows with a replay miss ({rows.Count(r => r.Meta.Replay is { Misses.Count: > 0 })}): top-1 {clean.Top1}, "
                          + $"top-5 {clean.Top5}, status {clean.Status}, match precision {clean.MatchPrecision}.");
            md.AppendLine();
        }
    }

    private static void Categories(StringBuilder md, IReadOnlyList<ResultRow> rows)
    {
        md.AppendLine("## By category");
        md.AppendLine();
        md.AppendLine("| Category | n | top-1 | top-5 | status | `match` @ precision | false match |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (cat, q) in ByCategory(rows))
            md.AppendLine($"| {cat} | {q.Rows} | {q.Top1} | {q.Top5} | {q.Status} | {q.MatchPrecision.Total} @ {Fmt.Rate(q.MatchPrecision.Value)} | {q.FalseMatches} |");
        md.AppendLine();
    }

    private static void Replay(StringBuilder md, IReadOnlyList<ResultRow> rows)
    {
        var withReplay = rows.Where(r => r.Meta.Replay is not null).ToList();
        var misses = withReplay.Where(r => r.Meta.Replay!.Misses.Count > 0).ToList();
        md.AppendLine("## Replay");
        md.AppendLine();
        md.AppendLine($"Requests answered from recordings: {withReplay.Sum(r => r.Meta.Replay!.Hits)} (of which fetched from the backend and cached in this run: "
                      + $"{withReplay.Sum(r => r.Meta.Replay!.Fills)}); unsupported (answered empty): {withReplay.Sum(r => r.Meta.Replay!.Unsupported)}; **replay_miss: {misses.Sum(r => r.Meta.Replay!.Misses.Count)} "
                      + $"requests on {misses.Count} rows** (a miss returned an empty result; those rows' scores are not trustworthy).");
        var partial = withReplay.Where(r => r.Meta.Replay!.Partial.Count > 0).ToList();
        md.AppendLine($"replay_partial (a recording shorter than the limit asked, or without person): {partial.Sum(r => r.Meta.Replay!.Partial.Count)} requests on {partial.Count} rows"
                      + (partial.Count == 0 ? "." : $": {string.Join("; ", partial.Take(10).Select(r => $"{r.Id} {string.Join(" · ", r.Meta.Replay!.Partial.Distinct())}"))}"));
        md.AppendLine();
        if (misses.Count > 0)
        {
            md.AppendLine("| Row | Query | Missing requests |");
            md.AppendLine("|---|---|---|");
            foreach (var r in misses.Take(40))
                md.AppendLine($"| {r.Id} | {Fmt.Cell(r.Query)} | {Fmt.Cell(string.Join(" · ", r.Meta.Replay!.Misses.Distinct()))} |");
            md.AppendLine();
        }
    }

    private static void Judge(StringBuilder md, IReadOnlyList<ResultRow> run, RunInfo info)
    {
        var judged = run.Where(r => r.Meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) }).ToList();
        md.AppendLine("## Judge");
        md.AppendLine();
        if (!info.Host.Judge && judged.Count == 0)
        {
            md.AppendLine("Off in this run (n/a).");
            md.AppendLine();
            return;
        }
        var byStatus = run.GroupBy(r => r.Meta.Judge?.Status.ToString() ?? "none").OrderBy(g => g.Key).Select(g => $"{EvalRunner.Snake(g.Key)} {g.Count()}");
        var decided = judged.Count(r => r.Meta.Judge!.Status is JudgeStatus.Agreed or JudgeStatus.Disagreed or JudgeStatus.ChoseNone);
        var agreed = judged.Count(r => r.Meta.Judge!.Status == JudgeStatus.Agreed);
        // Judge pick vs code top-1, on the rows where the judge picked a work and the row names acceptable work ids.
        var picked = judged.Where(r => r.Meta.Judge!.Pick is not null && r.Expected.Top1.Count > 0).ToList();
        var pickRight = picked.Count(r => r.Expected.Top1.Contains(r.Meta.Judge!.Pick!));
        var codeRight = picked.Count(r => r.Pass.Top1 == true);
        md.AppendLine($"- Verdicts: {string.Join(", ", byStatus)}");
        md.AppendLine($"- Agreement with code's #1: {new Ratio(agreed, decided)} (agreed / agreed + disagreed + chose_none)");
        md.AppendLine($"- On {picked.Count} judged rows with a pick and known ids: judge pick right {pickRight}, code #1 right {codeRight}");
        md.AppendLine($"- Second-opinion slots shown: {run.Count(r => r.Top5.Any(s => s.SecondOpinion))}");
        var p50 = Fmt.Percentile(judged.Select(r => (double)r.Meta.Judge!.ElapsedMs), 50);
        md.AppendLine($"- Judge latency p50 / p95: {Fmt.Ms(p50)} / {Fmt.Ms(Fmt.Percentile(judged.Select(r => (double)r.Meta.Judge!.ElapsedMs), 95))}");
        md.AppendLine();
    }

    private static void Explanations(StringBuilder md, IReadOnlyList<ResultRow> run)
    {
        var llm = run.Sum(r => r.Meta.ExplanationsLlm);
        var template = run.Sum(r => r.Meta.ExplanationsTemplate);
        var rejected = run.Sum(r => r.Meta.TemplateAfterLlmOk);
        md.AppendLine("## Explanations");
        md.AppendLine();
        md.AppendLine($"- Source mix (shown candidates): llm {llm}, template {template} ({Fmt.Rate(llm + template == 0 ? null : (double)llm / (llm + template))} llm)");
        md.AppendLine($"- Grounding rejections (template shown although the explain call succeeded): {rejected}");
        md.AppendLine($"- Rows with explanation_fallback (explain call failed/unavailable): {run.Count(r => r.Meta.Degraded.Contains(DegradedReason.ExplanationFallback))}");
        md.AppendLine();
    }

    private static void Degraded(StringBuilder md, IReadOnlyList<ResultRow> run)
    {
        var searched = run.Where(r => r.Meta.ElapsedMs is not null).ToList();
        var degraded = searched.Count(r => r.Meta.Degraded.Count > 0);
        md.AppendLine("## Degraded");
        md.AppendLine();
        md.AppendLine($"- Degraded rate: {new Ratio(degraded, searched.Count)}");
        var bySetting = searched.Count(r => r.Meta.Degraded.All(d => d is DegradedReason.AiNotConfigured or DegradedReason.ExplanationFallback) && r.Meta.Degraded.Count > 0);
        if (bySetting > 0)
            md.AppendLine($"  - of which {bySetting} only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)");
        var providerErrors = searched.Count(r => EvalRunner.HasProviderError(r.Meta));
        md.AppendLine($"- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): {new Ratio(providerErrors, searched.Count)}");
        var reserve = searched.Sum(r => r.Meta.ExplainReserveFallbacks);
        if (reserve > 0) md.AppendLine($"- Explain calls refused by the API's explain reserve (template shown, by design): {reserve}");
        foreach (var g in searched.SelectMany(r => r.Meta.Degraded).GroupBy(x => x).OrderBy(g => g.Key))
            md.AppendLine($"- {EvalRunner.Snake(g.Key.ToString())}: {g.Count()}");
        var interp = searched.GroupBy(r => r.Meta.InterpretationSource?.ToString() ?? "n/a").Select(g => $"{g.Key.ToLowerInvariant()} {g.Count()}");
        md.AppendLine($"- Interpretation source: {Joined(interp)}");
        md.AppendLine();
    }

    private static void Latency(StringBuilder md, IReadOnlyList<ResultRow> run, RunInfo info)
    {
        var searched = run.Where(r => r.Meta.ElapsedMs is not null).ToList();
        md.AppendLine("## Latency");
        md.AppendLine();
        if (info.Host.Catalog == CatalogMode.Replay)
            md.AppendLine("Replay: Open Library time is 0 by construction, so totals and `retrieve` are not Open Library latency.\n");
        md.AppendLine("| Measure | n | p50 | p95 | max |");
        md.AppendLine("|---|---|---|---|---|");
        Line(md, "Wall per query (validate + scope + search)", run.Where(r => r.Status is not (Outcomes.Rejected)).Select(r => (double)r.Meta.WallMs));
        Line(md, "Pipeline elapsed_ms", searched.Select(r => (double)r.Meta.ElapsedMs!.Value));
        foreach (var stage in searched.SelectMany(r => r.Meta.TimingsMs.Keys).Distinct().Order(StringComparer.Ordinal))
            Line(md, $"stage `{stage}`", searched.Where(r => r.Meta.TimingsMs.ContainsKey(stage)).Select(r => (double)r.Meta.TimingsMs[stage]));
        if (info.Host.Catalog != CatalogMode.Replay)
            Line(md, "Open Library call (not cache hits)", searched.SelectMany(r => r.Meta.CatalogCalls).Where(c => !c.CacheHit).Select(c => (double)c.ElapsedMs));
        foreach (var op in searched.SelectMany(r => r.Meta.LlmCalls).Select(l => l.Operation).Distinct().Order(StringComparer.Ordinal))
            Line(md, $"LLM `{op}` (not cache hits)", searched.SelectMany(r => r.Meta.LlmCalls).Where(l => l.Operation == op && !l.CacheHit && l.Status != LlmCallStatus.NotConfigured).Select(l => (double)l.ElapsedMs));
        md.AppendLine();
    }

    private static void Line(StringBuilder md, string name, IEnumerable<double> values)
    {
        var v = values.ToList();
        md.AppendLine($"| {name} | {v.Count} | {Fmt.Ms(Fmt.Percentile(v, 50))} | {Fmt.Ms(Fmt.Percentile(v, 95))} | {Fmt.Ms(v.Count == 0 ? null : v.Max())} |");
    }

    private static void OpenLibrary(StringBuilder md, IReadOnlyList<ResultRow> run, RunInfo info)
    {
        var searched = run.Where(r => r.Meta.ElapsedMs is not null).ToList();
        var calls = searched.SelectMany(r => r.Meta.CatalogCalls).ToList();
        md.AppendLine("## Open Library calls");
        md.AppendLine();
        md.AppendLine($"- Logical calls per query: mean {Fmt.Num(Fmt.Mean(searched.Select(r => (double)r.Meta.CatalogCalls.Count)), "0.00")}, "
                      + $"max {(searched.Count == 0 ? "n/a" : searched.Max(r => r.Meta.CatalogCalls.Count).ToString())}, "
                      + $"at budget {searched.Count(r => r.Meta.CatalogCallBudget is { } b && r.Meta.CatalogCalls.Count >= b)} "
                      + $"(budget: {Joined(searched.Select(r => r.Meta.CatalogCallBudget).Distinct().Select(b => b?.ToString() ?? "n/a"))} logical calls)");
        md.AppendLine($"- Cache hits: {new Ratio(calls.Count(c => c.CacheHit), calls.Count)}");
        md.AppendLine(info.Host.Catalog == CatalogMode.Replay
            ? "- HTTP attempts: n/a (replay)"
            : $"- HTTP attempts: {calls.Sum(c => c.Attempts)} total, mean {Fmt.Num(Fmt.Mean(searched.Select(r => (double)r.Meta.CatalogCalls.Sum(c => c.Attempts))), "0.00")} per query; slow-empty re-queries {calls.Count(c => c.SlowEmptyRetried)}");
        md.AppendLine($"- Outcomes: {Joined(calls.GroupBy(c => c.Outcome).OrderBy(g => g.Key).Select(g => $"{EvalRunner.Snake(g.Key.ToString())} {g.Count()}"))}");
        md.AppendLine($"- Steps: {Joined(calls.GroupBy(c => c.Step).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"))}");
        md.AppendLine();
    }

    private static void Llm(StringBuilder md, IReadOnlyList<ResultRow> run)
    {
        var searched = run.Where(r => r.Meta.ElapsedMs is not null).ToList();
        var calls = searched.SelectMany(r => r.Meta.LlmCalls).ToList();
        md.AppendLine("## LLM usage and cost");
        md.AppendLine();
        if (calls.Count == 0 && searched.All(r => r.Meta.Judge is null or { Status: JudgeStatus.Off or JudgeStatus.Skipped }))
        {
            md.AppendLine("No LLM calls in this run (n/a).");
            md.AppendLine();
            return;
        }
        md.AppendLine("| Operation | calls | cache hits | statuses | input tok | output tok |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var g in calls.GroupBy(l => l.Operation).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var statuses = string.Join(", ", g.GroupBy(l => l.Status).OrderBy(s => s.Key).Select(s => $"{EvalRunner.Snake(s.Key.ToString())} {s.Count()}"));
            md.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(l => l.CacheHit)} | {statuses} | {Tokens(g.Select(l => l.InputTokens))} | {Tokens(g.Select(l => l.OutputTokens))} |");
        }
        var judged = searched.Where(r => r.Meta.Judge is { Status: not (JudgeStatus.Off or JudgeStatus.Skipped) }).ToList();
        if (judged.Count > 0)
            md.AppendLine($"| judge | {judged.Count} | n/a | see Judge | {Tokens(judged.Select(r => r.Meta.Judge!.InputTokens))} | n/a |");
        md.AppendLine();
        var priced = searched.Where(r => r.Meta.EstCostUsd is not null).ToList();
        var total = priced.Sum(r => r.Meta.EstCostUsd!.Value);
        var unpriced = searched.Sum(r => r.Meta.UnpricedCalls);
        md.AppendLine($"- Paid LLM calls (not cache hits, incl. judge): {searched.Sum(r => EvalRunner.PaidLlmCalls(r.Meta))}");
        md.AppendLine(priced.Count == 0
            ? $"- Estimated cost: n/a (no priced calls{(unpriced > 0 ? $"; {unpriced} calls had no price" : "")})"
            : $"- Estimated cost (IAiPricing, paid-tier list prices): {Fmt.Usd(total)} for {searched.Count} searches = **{Fmt.Usd(total / searched.Count * 1000)} per 1,000 searches**"
              + (unpriced > 0 ? $" (lower bound: {unpriced} calls unpriced)" : ""));
        md.AppendLine();
    }

    private static string Tokens(IEnumerable<long?> values)
    {
        var list = values.ToList();
        var known = list.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (known.Count == 0) return "n/a";
        return $"{known.Sum()}{(known.Count < list.Count ? $" ({list.Count - known.Count} unreported)" : "")}";
    }

    private static void Misses(StringBuilder md, IReadOnlyList<ResultRow> rows)
    {
        var failed = rows.Where(r => r.Status != Outcomes.Skipped && (r.Pass.Top1 == false || r.Pass.Status == false || r.Pass.FalseMatch)).ToList();
        md.AppendLine($"## Misses ({failed.Count})");
        md.AppendLine();
        if (failed.Count == 0)
        {
            md.AppendLine("None.");
            md.AppendLine();
            return;
        }
        md.AppendLine("| Row | Query | Expected | Got | Top-3 shown |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var r in failed)
        {
            var expected = $"{string.Join("/", r.Expected.Status)}{(r.Expected.Describe is { } x ? $" · {x}" : "")}";
            var got = r.Status + (r.Reason is { } reason ? $" ({reason})" : "") + (r.Pass.TargetRank is { } rank ? $", target at #{rank}" : "");
            var top = string.Join("; ", r.Top5.Take(3).Select(s => $"{s.WorkId} {Short(s.Title)} [{s.Tier}]"));
            md.AppendLine($"| {r.Id} | {Fmt.Cell(r.Query)} | {Fmt.Cell(expected)} | {Fmt.Cell(got)} | {Fmt.Cell(top)} |");
        }
        md.AppendLine();
    }

    private static string Joined(IEnumerable<string> parts)
    {
        var text = string.Join(", ", parts);
        return text.Length == 0 ? "n/a" : text;
    }

    private static string Short(string s) => s.Length <= 40 ? s : s[..39] + "…";

    private static void WriteRaw(string folder, IReadOnlyList<RowOutcome> outcomes)
    {
        Directory.CreateDirectory(folder);
        using var w = new StreamWriter(Path.Combine(folder, "outcomes.jsonl"));
        foreach (var o in outcomes)
        {
            var node = new JsonObject
            {
                ["id"] = o.Row.Id,
                ["query"] = o.Row.Query,
                ["status"] = o.Row.Status,
                ["error"] = o.Row.Error,
                ["outcome"] = o.Outcome is null ? null : Raw(o.Outcome),
            };
            w.WriteLine(node.ToJsonString(EvalJson.Options));
        }
    }

    /// <summary>The outcome with the ranking cut to its first 10 works (the rest only bloats the file).</summary>
    private static JsonNode? Raw(SearchOutcome o) =>
        JsonSerializer.SerializeToNode(o with { Ranking = o.Ranking with { Ranked = [.. o.Ranking.Ranked.Take(10)] } }, EvalJson.Options);
}
