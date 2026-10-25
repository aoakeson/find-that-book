using System.Text.Json.Nodes;
using FindThatBook.Eval.Cli;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Replay;
using FindThatBook.Eval.Reports;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;
using FindThatBook.Eval.Stability;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Eval;

/// <summary>The CLI commands. Each reads its options, builds the eval host, runs, writes a result folder.</summary>
public static class Commands
{
    public static IReadOnlySet<string> Flags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "heldout", "no-cache", "verbose", "replace", "quiet",
    };

    private static readonly string[] Providers = ["none", "gemini", "anthropic", "openai"];

    public static int Help(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine(error);
        Console.WriteLine("""
            Find That Book eval (dotnet run --project tools/FindThatBook.Eval -- <command> [options])

              run        Score a dataset through the API's own pipeline (QueryValidator → SearchService).
                         --set golden|golden-v1|hard|synthetic|heldout|<file.json|.jsonl>  (default golden = v2)
                         --heldout               required to run the sealed held-out set (results are labelled HELD-OUT)
                         --catalog live|mirror|replay   (default live)   --replay-from <dir> (repeatable)
                         --provider none|gemini|anthropic|openai (default none = LLM-off arm)  --model <id>
                         --judge on|off (default off)   --limit N   --concurrency N   --out <dir>   --label <text>
                         --max-llm-calls N|unlimited (default 20 paid calls)   --max-ol-requests N
                         --llm-rpm N (pace LLM calls; default 90% of AI:<Provider>:RequestsPerMinute, Gemini 15; 0 = off)
                         --max-provider-error-rate 0.2 (default with an LLM: stop when > 20% of rows hit provider errors)
                         --resume <run folder>   keep its rows, re-run only skipped and provider-error rows
                         --no-cache   --mirror-url <url>   --config Key=Value (repeatable)   --env Production   --verbose
                         --research <dir>   optional research archive (battle rounds, prototype caches); default ../research
                         --catalog-cache <dir>|off   record-on-miss disk cache (mirror default: eval/results/_catalog-cache/raw/mirror)
                         --hypotheses <results.jsonl|hyp_v3.jsonl>   replay interpretations (miss → hypothesis_miss, LLM-off row)
                         --hypotheses-fill live   interpret the queries the file lacks live (same build) instead
                         --explain llm|template   --ranker default|baseline   --ranker-off Switch,Switch
              compare    compare <dirA> <dirB> [--out <file.md>]   delta table per category, B − A
              stability  --mode interpret|retrieval|both --runs N (default 3) + run options; caches bypassed
              record     live Open Library, LLM off by default: saves every search.json response into fixtures
                         --set golden --out tests/fixtures/golden [--replace]
              fixtures   offline: replays the set on recorded responses (existing fixtures + research caches) and
                         writes only the recordings it used, trimmed, to --out (default tests/fixtures/golden)
              ablate     default arm, then one arm per ranker switch turned off (+ the baseline arm), on the same catalog
                         responses (record-on-miss cache or replay) and the same hypotheses: per-switch delta table
                         [--switches A,B] + run options (--set, --catalog, --hypotheses, --limit, --label)
              index      rebuild eval/RESULTS.md from every eval/results/*/summary.json [--builds src1,src2] [--binaries bin1]: final build prints
            """);
        return error is null ? 0 : 1;
    }

    /// <summary>Options every command accepts: --research &lt;dir&gt; (the optional research archive).</summary>
    private static void Global(Args a)
    {
        if (a.Value("research") is { } research) RepoPaths.ResearchOverride = Path.GetFullPath(research);
    }

    public static async Task<int> RunAsync(Args a, string[] argv, CancellationToken ct)
    {
        Global(a);
        var dataset = LoadDataset(a);
        var host = HostOptions(a);
        var settings = RunSettingsOf(a, host);
        var resume = a.Value("resume") is { } rs ? Path.GetFullPath(rs) : null;
        var label = a.Value("label") ?? DefaultLabel(dataset, host) + (resume is null ? "" : "-resumed");
        var outDir = a.Value("out") ?? UniqueDir(Path.Combine(RepoPaths.Results, $"{DateTime.Now:yyyy-MM-dd}-{label}"));
        var quiet = a.Flag("quiet");
        a.RejectUnknown();
        await CheckMirrorAsync(host, ct);

        // --resume <folder>: keep that run's rows, re-run only the skipped ones and the ones a provider error spoiled.
        var prior = resume is null ? null : LoadRows(resume);
        var keep = prior?.Where(r => r.Status != Outcomes.Skipped && !EvalRunner.HasProviderError(r.Meta)).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var toRun = keep is null ? dataset : dataset with { Cases = [.. dataset.Cases.Where(c => !keep.ContainsKey(c.Id))] };

        await using var eval = await EvalHost.StartAsync(host, ct);
        Console.WriteLine($"run: {dataset.Name} v{dataset.Version} ({toRun.Cases.Count} cases{(keep is null ? "" : $", {keep.Count} kept from {RepoPaths.Relative(resume!)}")}){(dataset.HeldOut ? " [HELD-OUT]" : "")} · catalog {host.Catalog} · provider {host.Provider} · judge {(host.Judge ? "on" : "off")}");
        if (eval.Replay is { } r) Console.WriteLine($"replay index: {r.Index.Count} recordings from {r.Index.Sources.Count} folders");
        var runner = new EvalRunner(eval, settings);
        var result = await runner.RunAsync(toRun, quiet ? null : Progress(toRun.Cases.Count), ct);
        if (keep is not null)
        {
            var fresh = result.Rows.ToDictionary(x => x.Row.Id, StringComparer.Ordinal);
            result = result with { Rows = [.. dataset.Cases.Select(c => keep.TryGetValue(c.Id, out var k) ? new RowOutcome(k, null) : fresh[c.Id])] };
        }
        var info = Info(a, argv, label, dataset, host, settings, result, eval) with
        {
            LlmPace = runner.LlmPace,
            ResumedFrom = resume,
            ResumedRows = keep is null ? 0 : toRun.Cases.Count,
        };
        SummaryWriter.Write(outDir, info, result.Rows);
        PrintHeadline(result, outDir);
        return result.StoppedReason?.StartsWith("pipeline not implemented", StringComparison.Ordinal) == true ? 3 : 0;
    }

    public static int Compare(Args a)
    {
        Global(a);
        if (a.Positional.Count != 2) return Help("compare needs two result folders: compare <dirA> <dirB>");
        var outFile = a.Value("out");
        a.RejectUnknown();
        var md = CompareCommand.Run(a.Positional[0], a.Positional[1]);
        Console.WriteLine(md);
        if (outFile is not null) File.WriteAllText(outFile, md);
        return 0;
    }

    public static async Task<int> StabilityAsync(Args a, string[] argv, CancellationToken ct)
    {
        Global(a);
        var mode = a.Value("mode", "both");
        if (mode is not ("interpret" or "retrieval" or "both")) return Help("--mode must be interpret, retrieval or both");
        var runs = a.Int("runs") ?? 3;
        if (runs < 2) return Help("--runs must be at least 2");
        var dataset = LoadDataset(a);
        var label = a.Value("label") ?? $"stability-{SetName(dataset)}-{mode}";
        var outDir = a.Value("out") ?? UniqueDir(Path.Combine(RepoPaths.Results, $"{DateTime.Now:yyyy-MM-dd}-{label}"));
        var baseHost = HostOptions(a) with { BypassCaches = true };
        var s = new StabilityCommand.Settings(runs, a.Budget("max-llm-calls", 20), a.Budget("max-ol-requests", null), a.Int("concurrency") ?? 1,
            a.Int("llm-rpm"));
        a.RejectUnknown();
        var command = "stability " + string.Join(' ', argv.Skip(1));

        if (mode is "interpret" or "both")
        {
            if (baseHost.Provider == "none") return Help("interpretation stability needs an LLM: pass --provider gemini|anthropic|openai");
            await using var eval = await EvalHost.StartAsync(baseHost, ct);
            Console.WriteLine(await StabilityCommand.InterpretAsync(eval, dataset, s, outDir, command, ct));
        }
        if (mode is "retrieval" or "both")
        {
            // Retrieval variability isolates Open Library: the LLM is off unless --retrieval-provider says otherwise.
            var retrievalHost = baseHost with { Provider = a.Value("retrieval-provider", "none"), Judge = false };
            await CheckMirrorAsync(retrievalHost, ct);
            await using var eval = await EvalHost.StartAsync(retrievalHost, ct);
            Console.WriteLine(await StabilityCommand.RetrievalAsync(eval, dataset, s, outDir, command, ct));
        }
        Console.WriteLine($"written: {RepoPaths.Relative(outDir)}");
        return 0;
    }

    public static async Task<int> RecordAsync(Args a, string[] argv, CancellationToken ct)
    {
        Global(a);
        var dataset = LoadDataset(a, allowHeldOut: false);
        var fixtures = Path.GetFullPath(a.Value("out") ?? RepoPaths.GoldenFixtures);
        var replace = a.Flag("replace");
        var label = a.Value("label") ?? $"record-{SetName(dataset)}";
        var resultDir = UniqueDir(Path.Combine(RepoPaths.Results, $"{DateTime.Now:yyyy-MM-dd}-{label}"));
        var staging = Path.Combine(resultDir, "raw", "recordings");
        var host = HostOptions(a, CatalogMode.Live) with { RecordTo = staging };
        if (host.Catalog != CatalogMode.Live) return Help("record always uses the live catalog");
        var settings = RunSettingsOf(a, host) with { Concurrency = a.Int("concurrency") ?? 2 };
        a.RejectUnknown();

        await using var eval = await EvalHost.StartAsync(host, ct);
        Console.WriteLine($"record: {dataset.Cases.Count} cases against live Open Library (provider {host.Provider}) → {RepoPaths.Relative(fixtures)}");
        var runner = new EvalRunner(eval, settings);
        var result = await runner.RunAsync(dataset, Progress(dataset.Cases.Count), ct);
        SummaryWriter.Write(resultDir, Info(a, argv, label, dataset, host, settings, result, eval) with { LlmPace = runner.LlmPace }, result.Rows);

        // Attribute recordings to rows by request parameters (the adapter records from inside its cache, where the
        // per-query context does not flow): a row used a file when one of its catalog calls had the file's parameters.
        var rowKeys = result.Rows.Select(r => (r.Row.Id, Keys: r.Row.Meta.CatalogCalls.Select(c => RequestKey.FromTrace(c.Request).IndexKey).ToHashSet(StringComparer.Ordinal))).ToList();
        var recorded = RecordedResponseIndex.Load([staging]);
        var entries = Directory.Exists(staging)
            ? Directory.EnumerateFiles(staging, "search_*.json").Select(f => RecordedResponseIndex.TryRead(f, 0)).OfType<RecordedResponse>()
                .Select(r => new FixtureEntry(r with { Origin = "live " + (r.RecordedAt ?? "") }, r.Request.Limit ?? r.Docs.Count,
                    [.. rowKeys.Where(x => x.Keys.Contains(r.Request.Key.IndexKey)).Select(x => x.Id).Order(StringComparer.Ordinal)]))
                .ToList()
            : [];
        var written = FixtureWriter.Write(fixtures, entries, replace, Build("record", argv, dataset, host, entries.Count, new Dictionary<string, IReadOnlyList<string>>()));
        Console.WriteLine($"recorded {recorded.Count} responses; wrote {written} fixture files ({(replace ? "replaced" : "merged")}) to {RepoPaths.Relative(fixtures)}");
        PrintHeadline(result, resultDir);
        return 0;
    }

    public static async Task<int> FixturesAsync(Args a, string[] argv, CancellationToken ct)
    {
        Global(a);
        var dataset = LoadDataset(a, allowHeldOut: false);
        var fixtures = Path.GetFullPath(a.Value("out") ?? RepoPaths.GoldenFixtures);
        var sources = a.Values("replay-from").Select(Path.GetFullPath).ToList();
        if (sources.Count == 0) sources = [fixtures, .. RepoPaths.HasResearch ? RepoPaths.ResearchCaches : []];
        var host = HostOptions(a, CatalogMode.Replay) with { ReplaySources = sources, BlockNetwork = true };
        var settings = RunSettingsOf(a, host) with { Concurrency = 4 };
        a.RejectUnknown();

        await using var eval = await EvalHost.StartAsync(host, ct);
        var replay = eval.Replay!;
        Console.WriteLine($"fixtures: {dataset.Name} v{dataset.Version} replayed on {replay.Index.Count} recordings (provider {host.Provider}) → {RepoPaths.Relative(fixtures)}");
        var result = await new EvalRunner(eval, settings).RunAsync(dataset, null, ct);
        if (result.StoppedReason is { } stopped)
        {
            Console.Error.WriteLine($"not written: {stopped}");
            return 3;
        }
        var entries = replay.Used.Select(u => new FixtureEntry(u.Response, u.MaxLimit, u.Cases)).ToList();
        var misses = replay.Misses;
        var partial = result.Rows.Where(r => r.Row.Meta.Replay is { Partial.Count: > 0 }).ToList();
        var written = FixtureWriter.Write(fixtures, entries, replace: true, Build("fixtures", argv, dataset, host, entries.Count, misses));
        Console.WriteLine($"wrote {written} fixture files; {misses.Count} requests had no recording; {partial.Count} rows used a partial recording"
                          + (misses.Count == 0 ? "" : " (run `record` against live Open Library to fill them):"));
        foreach (var (request, cases) in misses.OrderBy(m => m.Key, StringComparer.Ordinal))
            Console.WriteLine($"  miss {request}  ← {string.Join(", ", cases)}");
        foreach (var r in partial) Console.WriteLine($"  partial {string.Join(" · ", r.Row.Meta.Replay!.Partial)}  ← {r.Row.Id}");
        var q = SummaryWriter.Quality(result.Rows.Select(r => r.Row));
        Console.WriteLine($"replay quality on this build: top-1 {q.Top1}, top-5 {q.Top5}, status {q.Status}, match precision {q.MatchPrecision}, false matches {q.FalseMatches}");
        return 0;
    }

    public static async Task<int> AblateAsync(Args a, string[] argv, CancellationToken ct)
    {
        Global(a);
        if (a.Value("render") is { } render)
        {
            // Re-render ablation.md/json from an existing ablation folder (no runs), e.g. after a table change.
            var renderSet = LoadDataset(a);
            var host0 = HostOptions(a) with { Explain = "template", Judge = false };
            a.RejectUnknown();
            var loaded = AblateCommand.Load(render);
            var measured = JsonNode.Parse(File.ReadAllText(Path.Combine(render, "default", "summary.json")))!["command"]?.GetValue<string>() ?? "n/a";
            var table = AblateCommand.Table(renderSet, host0, loaded, measured, TimeSpan.Zero, AblateCommand.Provenance(render));
            File.WriteAllText(Path.Combine(render, "ablation.md"), table);
            File.WriteAllText(Path.Combine(render, "ablation.json"), AblateCommand.Json(renderSet, loaded).ToJsonString(EvalJson.Indented) + "\n");
            Console.WriteLine(table);
            return 0;
        }
        var dataset = LoadDataset(a);
        var baseHost = HostOptions(a) with { Explain = "template", Judge = false };
        if (baseHost.InterpretsWithLlm)
            return Help("ablate never calls an LLM: pass --hypotheses <results.jsonl|hyp_v3.jsonl> (or --provider none for the LLM-off ablation)");
        var switches = a.Value("switches") is { } sw
            ? sw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [.. RankerArms.Switches];
        foreach (var s in switches) RankerArms.Validate("default", [s]);
        var label = a.Value("label") ?? $"ablate-{SetName(dataset)}-{(baseHost.Hypotheses is null ? "llm-off" : "hypotheses")}";
        var outDir = a.Value("out") ?? UniqueDir(Path.Combine(RepoPaths.Results, $"{DateTime.Now:yyyy-MM-dd}-{label}"));
        var settings = RunSettingsOf(a, baseHost);
        a.RejectUnknown();
        await CheckMirrorAsync(baseHost, ct);

        var command = "dotnet run --project tools/FindThatBook.Eval -- " + string.Join(' ', argv);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var arms = new List<AblateCommand.ArmResult>();
        var olUsed = 0; // --max-ol-requests is a budget for the whole ablation, not per arm
        foreach (var arm in AblateCommand.Arms(switches))
        {
            var host = baseHost with { Ranker = arm.Ranker, RankerOff = arm.Off };
            await using var eval = await EvalHost.StartAsync(host, ct);
            var runner = new EvalRunner(eval, settings.MaxCatalogRequests is { } olMax ? settings with { MaxCatalogRequests = Math.Max(0, olMax - olUsed) } : settings);
            var result = await runner.RunAsync(dataset, null, ct);
            if (result.StoppedReason is { } stopped) throw new InvalidOperationException($"arm {arm.Name} stopped: {stopped}");
            // The default and baseline arms are runs in their own right (committed, indexed); switch arms stay in raw/.
            var folder = arm.Off.Count == 0
                ? Path.Combine(outDir, arm.Ranker)
                : Path.Combine(outDir, "raw", "arms", arm.Name.Trim('-').ToLowerInvariant());
            var info = Info(a, argv, $"{label} {arm.Name}", dataset, host, settings, result, eval);
            SummaryWriter.Write(folder, info, result.Rows);
            var rows = result.Rows.Select(r => r.Row).ToList();
            olUsed += rows.Sum(r => r.Meta.CatalogCalls.Sum(c => c.Attempts));
            var q = SummaryWriter.Quality(rows);
            arms.Add(new AblateCommand.ArmResult(arm, q, rows, folder));
            Console.WriteLine($"[{arms.Count,2}/{switches.Count + 2}] {arm.Name,-32} top-1 {q.Top1} · status {q.Status} · match {q.MatchPrecision} · severe {q.SevereMatches} · errors {q.Errors} · fetched {info.CacheFills} · HTTP so far {olUsed}");
        }
        var md = AblateCommand.Table(dataset, baseHost, arms, command, clock.Elapsed);
        File.WriteAllText(Path.Combine(outDir, "ablation.md"), md);
        File.WriteAllText(Path.Combine(outDir, "ablation.json"), AblateCommand.Json(dataset, arms).ToJsonString(EvalJson.Indented) + "\n");
        Console.WriteLine();
        Console.WriteLine(md);
        Console.WriteLine($"written: {RepoPaths.Relative(outDir)}/ablation.md");
        return 0;
    }

    public static int Index(Args a)
    {
        Global(a);
        var builds = a.Value("builds")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var binaries = a.Value("binaries")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        a.RejectUnknown();
        var path = Path.Combine(RepoPaths.Eval, "RESULTS.md");
        File.WriteAllText(path, IndexCommand.Build(builds, binaries));
        Console.WriteLine($"written: {RepoPaths.Relative(path)}");
        return 0;
    }

    private static System.Text.Json.Nodes.JsonObject Build(string command, string[] argv, Dataset d, EvalHostOptions host, int files,
        IReadOnlyDictionary<string, IReadOnlyList<string>> misses) => new()
    {
        ["command"] = command,
        ["args"] = string.Join(' ', argv.Skip(1)),
        ["date"] = RunInfo.Stamp(DateTimeOffset.Now),
        ["dataset"] = $"{d.Name} v{d.Version} sha256:{d.Sha256}",
        ["provider"] = host.Provider,
        ["code_fingerprint"] = CodeFingerprint.OfSource().ToString(),
        ["files_written"] = files,
        ["misses"] = new System.Text.Json.Nodes.JsonArray([.. misses.Keys.Order(StringComparer.Ordinal).Select(m => (System.Text.Json.Nodes.JsonNode)m)]),
    };

    private static Dataset LoadDataset(Args a, bool? allowHeldOut = null)
    {
        var dataset = DatasetLoader.Load(a.Value("set", "golden"), allowHeldOut ?? a.Flag("heldout"));
        if (allowHeldOut is null && a.Flag("heldout") && !dataset.HeldOut)
            throw new ArgumentException("--heldout was given but the set is not the held-out set");
        if (allowHeldOut == false && dataset.HeldOut) throw new HeldOutRefusedException(RepoPaths.Relative(dataset.Path));
        return dataset.Take(a.Int("limit"));
    }

    private static EvalHostOptions HostOptions(Args a, CatalogMode? force = null)
    {
        var provider = a.Value("provider", "none").ToLowerInvariant();
        if (!Providers.Contains(provider)) throw new ArgumentException($"--provider must be one of {string.Join(", ", Providers)}");
        var catalog = force ?? a.Value("catalog", "live").ToLowerInvariant() switch
        {
            "live" => CatalogMode.Live,
            "mirror" => CatalogMode.Mirror,
            "replay" or "fixtures" => CatalogMode.Replay,
            var c => throw new ArgumentException($"--catalog must be live, mirror or replay, got '{c}'"),
        };
        var sources = a.Values("replay-from").Select(Path.GetFullPath).ToList();
        // Default replay sources: the repo's own fixtures, then the research archive's caches when it is present.
        if (catalog == CatalogMode.Replay && sources.Count == 0)
            sources = [RepoPaths.GoldenFixtures, .. RepoPaths.HasResearch ? RepoPaths.ResearchCaches : []];
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in a.Values("config"))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) throw new ArgumentException($"--config expects Key=Value, got '{kv}'");
            settings[kv[..eq]] = kv[(eq + 1)..];
        }
        var explain = a.Value("explain", "llm").ToLowerInvariant();
        if (explain is not ("llm" or "template")) throw new ArgumentException("--explain must be llm or template");
        var rankerOff = a.Value("ranker-off") is { } off
            ? off.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];
        var ranker = a.Value("ranker", "default").ToLowerInvariant();
        RankerArms.Validate(ranker, rankerOff);
        var hypotheses = a.Value("hypotheses") is { } h ? Path.GetFullPath(h) : null;
        if (hypotheses is not null && !File.Exists(hypotheses)) throw new FileNotFoundException($"--hypotheses file not found: {hypotheses}");
        return new EvalHostOptions
        {
            Catalog = catalog,
            Provider = provider,
            Model = a.Value("model"),
            Judge = a.OnOff("judge", false),
            BypassCaches = a.Flag("no-cache"),
            ReplaySources = sources,
            CatalogCache = CacheFolder(a, catalog),
            Hypotheses = hypotheses,
            HypothesesFillLive = a.Value("hypotheses-fill") switch
            {
                null or "off" => false,
                "live" => true,
                var v => throw new ArgumentException($"--hypotheses-fill must be live or off, got '{v}'"),
            },
            Explain = explain,
            Ranker = ranker,
            RankerOff = rankerOff,
            MirrorUrl = a.Value("mirror-url", "http://127.0.0.1:8090/"),
            Environment = a.Value("env", "Production"),
            LogLevel = a.Flag("verbose") ? LogLevel.Information : LogLevel.Warning,
            Settings = settings,
        };
    }

    /// <summary>
    /// --catalog-cache &lt;dir&gt; | off. Mirror runs default to the per-backend record-on-miss cache (repeatable, fast arms);
    /// live runs use one only when asked; replay never does.
    /// </summary>
    private static string? CacheFolder(Args a, CatalogMode catalog)
    {
        var value = a.Value("catalog-cache");
        if (catalog == CatalogMode.Replay || value is "off") return null;
        if (value is not null) return Path.GetFullPath(value);
        return catalog == CatalogMode.Mirror ? EvalHost.DefaultCacheFolder(catalog) : null;
    }

    /// <summary>The rows of an earlier run folder (results.jsonl or raw/results.jsonl).</summary>
    public static IReadOnlyList<ResultRow> LoadRows(string folder)
    {
        var path = SummaryWriter.FindResults(folder) ?? throw new FileNotFoundException($"No results.jsonl in {folder}");
        return [.. File.ReadLines(path).Where(l => l.Length > 0).Select(l => System.Text.Json.JsonSerializer.Deserialize<ResultRow>(l, EvalJson.Options)!)];
    }

    private static RunSettings RunSettingsOf(Args a, EvalHostOptions host) => new()
    {
        Concurrency = a.Int("concurrency") ?? (host.Catalog == CatalogMode.Live ? 2 : host.Catalog == CatalogMode.Mirror ? 6 : 4),
        MaxLlmCalls = a.Budget("max-llm-calls", 20),
        MaxCatalogRequests = a.Budget("max-ol-requests", null),
        LlmCallsPerMinute = a.Int("llm-rpm"),
        MaxProviderErrorRate = a.Value("max-provider-error-rate") is { } rate
            ? double.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)
            : host.InterpretsWithLlm || host.ExplainsWithLlm ? 0.2 : null,
    };

    private static RunInfo Info(Args a, string[] argv, string label, Dataset dataset, EvalHostOptions host, RunSettings settings, RunResult result, EvalHost eval) => new()
    {
        Command = "dotnet run --project tools/FindThatBook.Eval -- " + string.Join(' ', argv.Select(x => x.Contains(' ') ? $"\"{x}\"" : x)),
        Label = label,
        Started = result.Started,
        Wall = result.Wall,
        Dataset = dataset,
        Host = host,
        Source = CodeFingerprint.OfSource(),
        Tool = CodeFingerprint.OfEvalTool(),
        Concurrency = settings.Concurrency,
        ResultLimit = settings.ResultLimit,
        StoppedReason = result.StoppedReason,
        ReplaySources = eval.Replay?.Index.Sources ?? [],
        Hypotheses = eval.Hypotheses?.Path,
        HypothesisEntries = eval.Hypotheses?.Count ?? 0,
        CacheFills = eval.Replay?.Fills ?? 0,
        Unsupported = eval.Replay?.UnsupportedCalls ?? 0,
    };

    private static string SetName(Dataset d) => d.Slug;

    private static string DefaultLabel(Dataset d, EvalHostOptions h) =>
        string.Join('-', new[] { SetName(d), h.Catalog.ToString().ToLowerInvariant(), h.Provider, h.Judge ? "judge" : null, d.HeldOut ? "HELDOUT" : null }
            .Where(x => x is not null));

    private static string UniqueDir(string dir)
    {
        if (!Directory.Exists(dir)) return dir;
        for (var i = 2; ; i++)
            if (!Directory.Exists($"{dir}-{i}")) return $"{dir}-{i}";
    }

    private static Action<ResultRow> Progress(int total)
    {
        var done = 0;
        var gate = new Lock();
        return row =>
        {
            lock (gate)
            {
                done++;
                var mark = row.Pass.Top1 switch { true => "top1 ok", false => "top1 MISS", null => "" };
                var status = row.Pass.Status switch { true => "status ok", false => "status MISS", null => "" };
                Console.WriteLine($"[{done,3}/{total}] {row.Id,-14} {row.Status,-9} {mark,-9} {status,-11} {row.Query}");
            }
        };
    }

    private static void PrintHeadline(RunResult result, string outDir)
    {
        var q = SummaryWriter.Quality(result.Rows.Select(r => r.Row));
        Console.WriteLine();
        Console.WriteLine($"top-1 {q.Top1} · top-5 {q.Top5} · status {q.Status} · match precision {q.MatchPrecision} · coverage {q.Coverage} · false matches {q.FalseMatches} · errors {q.Errors}");
        if (result.StoppedReason is { } s) Console.WriteLine($"STOPPED: {s}");
        Console.WriteLine($"written: {RepoPaths.Relative(outDir)}/summary.md");
    }

    private static async Task CheckMirrorAsync(EvalHostOptions host, CancellationToken ct)
    {
        if (host.Catalog != CatalogMode.Mirror) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(new Uri(new Uri(host.MirrorUrl), "health"), ct);
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        throw new InvalidOperationException($"The Open Library mirror is not answering at {host.MirrorUrl}. Start it first "
            + "(ol-mirror/server.py in the research archive, which is not part of this repo: python3 server.py 8090).");
    }
}
