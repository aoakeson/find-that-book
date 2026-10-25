using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Eval.Reports;

/// <summary>
/// Ranker ablation: the default arm once, then one arm per rule switch turned off, then the baseline arm (every addition
/// off = the measured prototype). Every arm sees the same catalog responses (record-on-miss cache or replay) and the
/// same interpretations (replayed hypotheses, or LLM off), so a delta is the rule's effect and nothing else.
/// "Off breaks" = rows the default arm passes that the arm fails (the rule earns its keep there); "off fixes" = the
/// reverse (the rule costs those rows). A row passes when top-1 and status are right and it has no severe match.
/// </summary>
public static class AblateCommand
{
    public sealed record Arm(string Name, string Ranker, IReadOnlyList<string> Off);

    public sealed record ArmResult(Arm Arm, QualityStats Quality, IReadOnlyList<ResultRow> Rows, string Folder);

    public static IReadOnlyList<Arm> Arms(IReadOnlyList<string> switches) =>
    [
        new("default", "default", []),
        .. switches.Select(s => new Arm($"-{s}", "default", [s])),
        new("baseline (every addition off)", "baseline", []),
    ];

    public static bool Passes(ResultRow r) =>
        r.Status is not (Outcomes.Skipped or Outcomes.Error) && r.Pass.Top1 != false && r.Pass.Status != false && !r.Pass.ToScore(r.Status).SevereMatch;

    /// <param name="provenance">For a re-render: the fingerprints and date the arms were measured with (from the default arm's summary.json).</param>
    public static string Table(Dataset dataset, EvalHostOptions host, IReadOnlyList<ArmResult> arms, string command, TimeSpan wall,
        string? provenance = null)
    {
        var baseArm = arms[0];
        var baseById = baseArm.Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var md = new StringBuilder();
        md.AppendLine($"# Ranker ablation: {dataset.Name} v{dataset.Version}");
        md.AppendLine();
        if (dataset.Label is { } label) md.AppendLine($"> {label}\n");
        if (dataset.HeldOut) md.AppendLine("> **HELD-OUT RESULTS.** Sealed set, final evaluation only.\n");
        if (provenance is not null) md.AppendLine(provenance);
        else
        {
            md.AppendLine($"- Date: {RunInfo.Stamp(DateTimeOffset.Now)} · wall {wall.TotalMinutes:0.0} min");
            md.AppendLine($"- Code fingerprint: `{CodeFingerprint.OfSource()}` · pipeline binaries `{CodeFingerprint.OfBinaries()}` · eval `{CodeFingerprint.OfEvalTool()}`");
        }
        md.AppendLine($"- Dataset: `{RepoPaths.Relative(dataset.Path)}` sha256:{dataset.Sha256}, {dataset.Cases.Count} rows, tuning: {dataset.Tuning}");
        md.AppendLine($"- Catalog: {host.Catalog.ToString().ToLowerInvariant()}{(host.CatalogCache is { } c ? $" behind the record-on-miss cache `{RepoPaths.Relative(c)}`" : "")}");
        md.AppendLine($"- Interpretations: {(host.Hypotheses is { } h ? $"replayed from `{Path.GetFileName(Path.GetDirectoryName(h))}/{Path.GetFileName(h)}`" : "none (LLM off)")} · explanations: template · judge: off");
        md.AppendLine($"- Command: `{Fmt.Cell(command)}`");
        md.AppendLine();
        var q0 = baseArm.Quality;
        md.AppendLine($"Default arm: top-1 {q0.Top1}, top-5 {q0.Top5}, status {q0.Status}, match {q0.MatchPrecision}, severe {q0.SevereMatches}, errors {q0.Errors}; "
                      + $"rows with an error or a failed catalog call in any arm (left out of breaks/fixes): {arms.SelectMany(a => a.Rows).Where(Unreliable).Select(r => r.Id).Distinct().Count()}.");
        md.AppendLine();
        md.AppendLine("Δ = arm − default, in points (1 point = 0.01); a negative Δ means the switched-off rule was helping. "
                      + "A row *passes* when top-1 and status are both right and it has no severe match; \"off breaks\" are rows that "
                      + "pass with the rule and fail without it, \"off fixes\" the reverse (rows run without error in both arms only). "
                      + "A rule can move one metric without flipping a whole row (for example turning a wrong match into an "
                      + "under-confident ambiguous), so read the severe column with the net.");
        md.AppendLine();
        md.AppendLine("| Arm (rule switched off) | top-1 Δ | top-5 Δ | status Δ | `match` n @ P (Δ P) | severe Δ | errors | off breaks | off fixes | net | verdict | broken by switching off (first 3) | fixed by switching off (first 3) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var a in arms.Skip(1).OrderByDescending(a => Net(a, baseById)).ThenBy(a => a.Arm.Name, StringComparer.Ordinal))
        {
            var q = a.Quality;
            var (breaks, fixes) = Flips(a, baseById);
            var net = breaks.Count - fixes.Count;
            var verdict = Verdict(net, q.SevereMatches - q0.SevereMatches, breaks.Count + fixes.Count,
                (q.Top1.Hits - q0.Top1.Hits) + (q.Status.Hits - q0.Status.Hits));
            md.AppendLine($"| `{a.Arm.Name}` | {Delta(q0.Top1, q.Top1)} | {Delta(q0.Top5, q.Top5)} | {Delta(q0.Status, q.Status)} | "
                          + $"{q.MatchPrecision.Total} @ {Fmt.Rate(q.MatchPrecision.Value)} ({Delta(q0.MatchPrecision, q.MatchPrecision)}) | "
                          + $"{Signed(q.SevereMatches - q0.SevereMatches)} | {q.Errors} | {breaks.Count} | {fixes.Count} | {Signed(net)} | {verdict} | "
                          + $"{Fmt.Cell(string.Join("; ", breaks.Take(3).Select(r => $"{r.Id} {Short(r.Query)}")))} | "
                          + $"{Fmt.Cell(string.Join("; ", fixes.Take(3).Select(r => $"{r.Id} {Short(r.Query)}")))} |");
        }
        md.AppendLine();
        md.AppendLine("Every broken / fixed row id per arm is in `ablation.json`; each arm's full results are under `raw/arms/` (gitignored). "
                      + $"One row here = {Fmt.Num(100.0 / Math.Max(1, dataset.Cases.Count), "0.00")} points.");
        return md.ToString();
    }

    /// <summary>Rebuilds the arm results from an ablation folder (default/, baseline/, raw/arms/*), to re-render the table.</summary>
    /// <summary>The default arm's measurement stamp, for a re-rendered table.</summary>
    public static string Provenance(string folder)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "default", "summary.json")))!;
        var started = DateTimeOffset.Parse(j["started"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        return $"- Measured: {RunInfo.Stamp(started)} (default arm start; table re-rendered {RunInfo.Stamp(DateTimeOffset.Now)})\n"
               + $"- Code fingerprint: `{j["code_fingerprint"]}` · pipeline binaries `{j["binary_fingerprint"] ?? "n/a"}` · eval `{j["eval_fingerprint"]}`";
    }

    public static IReadOnlyList<ArmResult> Load(string folder)
    {
        var arms = new List<ArmResult>();
        var dirs = new[] { Path.Combine(folder, "default") }
            .Concat(Directory.Exists(Path.Combine(folder, "raw", "arms")) ? Directory.EnumerateDirectories(Path.Combine(folder, "raw", "arms")).Order(StringComparer.Ordinal) : [])
            .Concat([Path.Combine(folder, "baseline"), Path.Combine(folder, "v3")])
            .Where(d => File.Exists(Path.Combine(d, "summary.json")));
        foreach (var dir in dirs)
        {
            var ranker = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "summary.json")))!["ranker"]?.GetValue<string>() ?? "default";
            var parts = ranker.Split(" -", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var baseArm = parts[0] is "v3" ? "baseline" : parts[0];
            var off = parts.Skip(1).ToList();
            var name = off.Count == 0 ? (baseArm == "default" ? "default" : "baseline (every addition off)") : $"-{string.Join(" -", off)}";
            var rows = Commands.LoadRows(dir);
            arms.Add(new ArmResult(new Arm(name, baseArm, off), QualityStats.Compute(rows.Select(r => (r.Status, r.Pass.ToScore(r.Status)))), rows, dir));
        }
        return arms;
    }

    public static JsonObject Json(Dataset dataset, IReadOnlyList<ArmResult> arms)
    {
        var baseById = arms[0].Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return new JsonObject
        {
            ["dataset"] = $"{dataset.Name} v{dataset.Version} sha256:{dataset.Sha256}",
            ["code_fingerprint"] = arms[0].Folder is { Length: > 0 } f && File.Exists(Path.Combine(f, "summary.json"))
                ? JsonNode.Parse(File.ReadAllText(Path.Combine(f, "summary.json")))!["code_fingerprint"]?.GetValue<string>()
                : CodeFingerprint.OfSource().ToString(),
            ["binary_fingerprint"] = arms[0].Folder is { Length: > 0 } g && File.Exists(Path.Combine(g, "summary.json"))
                ? JsonNode.Parse(File.ReadAllText(Path.Combine(g, "summary.json")))!["binary_fingerprint"]?.GetValue<string>()
                : CodeFingerprint.OfBinaries().ToString(),
            ["arms"] = new JsonArray([.. arms.Select(a =>
            {
                var (breaks, fixes) = Flips(a, baseById);
                return (JsonNode)new JsonObject
                {
                    ["arm"] = a.Arm.Name,
                    ["ranker"] = RankerArms.Describe(a.Arm.Ranker, a.Arm.Off),
                    ["quality"] = JsonSerializer.SerializeToNode(a.Quality, EvalJson.Options),
                    ["off_breaks"] = new JsonArray([.. breaks.Select(r => (JsonNode)r.Id)]),
                    ["off_fixes"] = new JsonArray([.. fixes.Select(r => (JsonNode)r.Id)]),
                    ["folder"] = RepoPaths.Relative(a.Folder),
                };
            })]),
        };
    }

    /// <summary>
    /// The rule's value from switching it off: severe matches it prevents count first (a wrong "match" is the costly
    /// error), then whole rows it wins or loses, then single-metric changes.
    /// </summary>
    public static string Verdict(int net, int severeDelta, int flips, int metricDelta) =>
        severeDelta > 0 && net >= 0 ? $"earns its keep (prevents {severeDelta} severe)"
        : severeDelta > 0 ? $"trade-off (prevents {severeDelta} severe, costs {-net} rows)"
        : net > 0 ? "earns its keep" + (severeDelta < 0 ? $" (but {-severeDelta} more severe)" : "")
        : net < 0 ? "costs rows here" + (severeDelta < 0 ? $" and {-severeDelta} severe" : "")
        : flips == 0 && metricDelta == 0 && severeDelta == 0 ? "no effect here"
        : "mixed (no whole-row change)";

    private static int Net(ArmResult a, IReadOnlyDictionary<string, ResultRow> baseById)
    {
        var (b, f) = Flips(a, baseById);
        return b.Count - f.Count;
    }

    public static (List<ResultRow> Breaks, List<ResultRow> Fixes) Flips(ArmResult a, IReadOnlyDictionary<string, ResultRow> baseById)
    {
        var breaks = new List<ResultRow>();
        var fixes = new List<ResultRow>();
        foreach (var r in a.Rows)
        {
            // A row that errored or had a failed catalog call in either arm (a timeout, a 5xx) says nothing about the
            // rule: left out of the flips, counted separately.
            if (!baseById.TryGetValue(r.Id, out var b) || Unreliable(b) || Unreliable(r)) continue;
            var before = Passes(b);
            var after = Passes(r);
            if (before && !after) breaks.Add(r);
            else if (!before && after) fixes.Add(r);
        }
        return (breaks, fixes);
    }

    public static bool Unreliable(ResultRow r) =>
        r.Status is Outcomes.Skipped or Outcomes.Error
        || r.Meta.CatalogCalls.Any(c => c.Outcome != FindThatBook.Core.Search.CatalogCallOutcome.Ok);

    private static string Delta(Ratio a, Ratio b)
    {
        if (a.Value is not { } x || b.Value is not { } y) return "n/a";
        var points = (y - x) * 100;
        return Math.Abs(points) < 0.05 ? "=" : points.ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
    }

    private static string Signed(int n) => n == 0 ? "=" : n.ToString("+0;-0", CultureInfo.InvariantCulture);

    private static string Short(string s) => s.Length <= 32 ? s : s[..31] + "…";
}
