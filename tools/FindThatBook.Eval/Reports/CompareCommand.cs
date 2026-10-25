using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Eval.Reports;

/// <summary>
/// compare &lt;dirA&gt; &lt;dirB&gt;: two result folders → one delta table per category (B − A, in points) plus the rows that
/// flipped. Typical use: the same set with --provider none vs --provider gemini (LLM on vs off).
/// </summary>
public static class CompareCommand
{
    public sealed record Row(string Id, string Query, string Category, string Status, CaseScore Score);

    public static string Run(string dirA, string dirB)
    {
        var allA = Load(dirA);
        var allB = Load(dirB);
        // Only rows run in both folders are compared, so a --limit run or a skipped row never fakes a delta.
        var shared = allA.Select(r => r.Id).Intersect(allB.Select(r => r.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var a = allA.Where(r => shared.Contains(r.Id)).ToList();
        var b = allB.Where(r => shared.Contains(r.Id)).ToList();
        var metaA = Meta(dirA);
        var metaB = Meta(dirB);
        var md = new StringBuilder();
        md.AppendLine($"# Compare: A = `{Path.GetFileName(Path.TrimEndingDirectorySeparator(dirA))}` → B = `{Path.GetFileName(Path.TrimEndingDirectorySeparator(dirB))}`");
        md.AppendLine();
        md.AppendLine("| | A | B |");
        md.AppendLine("|---|---|---|");
        foreach (var key in new[] { "dataset", "catalog", "provider", "model", "observed_models", "judge", "code_fingerprint", "started" })
            md.AppendLine($"| {key} | {Describe(metaA?[key])} | {Describe(metaB?[key])} |");
        md.AppendLine();
        var sameData = Describe(metaA?["dataset"]) == Describe(metaB?["dataset"]);
        if (!sameData) md.AppendLine("> **Different datasets**: rows are paired by id only.\n");
        if (shared.Count < allA.Count || shared.Count < allB.Count)
            md.AppendLine($"> Compared on the {shared.Count} rows run in both (A ran {allA.Count}, B ran {allB.Count}); the rest are left out.\n");

        md.AppendLine("| Category | n A/B | top-1 A → B | top-5 A → B | status A → B | `match` @ P A → B | false match A → B |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        var cats = a.Select(r => r.Category).Concat(b.Select(r => r.Category)).Distinct().Order(StringComparer.Ordinal).ToList();
        Line(md, "**overall**", a, b);
        foreach (var cat in cats) Line(md, cat, a.Where(r => r.Category == cat).ToList(), b.Where(r => r.Category == cat).ToList());
        md.AppendLine();

        var byId = a.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var paired = b.Select(r => (A: byId[r.Id], B: r)).ToList();
        Flips(md, "Top-1 fixed (A miss → B hit)", paired.Where(p => p.A.Score.Top1 == false && p.B.Score.Top1 == true));
        Flips(md, "Top-1 broken (A hit → B miss)", paired.Where(p => p.A.Score.Top1 == true && p.B.Score.Top1 == false));
        Flips(md, "Status fixed", paired.Where(p => p.A.Score.Status == false && p.B.Score.Status == true));
        Flips(md, "Status broken", paired.Where(p => p.A.Score.Status == true && p.B.Score.Status == false));
        Flips(md, "New wrong or false matches in B", paired.Where(p => p.B.Score.IsMatch && p.B.Score.MatchCorrect == false && !(p.A.Score.IsMatch && p.A.Score.MatchCorrect == false)));
        md.AppendLine("Deltas are B − A in points (1 point = 0.01). On the 60-row golden set one query moves top-1 by about 1.8 points.");
        return md.ToString();
    }

    private static void Line(StringBuilder md, string name, IReadOnlyList<Row> a, IReadOnlyList<Row> b)
    {
        var qa = QualityStats.Compute(a.Select(r => (r.Status, r.Score)));
        var qb = QualityStats.Compute(b.Select(r => (r.Status, r.Score)));
        md.AppendLine($"| {name} | {qa.Rows}/{qb.Rows} | {Delta(qa.Top1, qb.Top1)} | {Delta(qa.Top5, qb.Top5)} | {Delta(qa.Status, qb.Status)} | "
                      + $"{qa.MatchPrecision.Total} @ {Fmt.Rate(qa.MatchPrecision.Value)} → {qb.MatchPrecision.Total} @ {Fmt.Rate(qb.MatchPrecision.Value)} | "
                      + $"{qa.FalseMatches} → {qb.FalseMatches} |");
    }

    private static string Delta(Ratio a, Ratio b)
    {
        var text = $"{Fmt.Rate(a.Value)} → {Fmt.Rate(b.Value)}";
        if (a.Value is not { } x || b.Value is not { } y) return text;
        var points = (y - x) * 100;
        return Math.Abs(points) < 0.05 ? text + " (=)" : text + $" ({points.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)})";
    }

    private static void Flips(StringBuilder md, string title, IEnumerable<(Row A, Row B)> pairs)
    {
        var list = pairs.ToList();
        md.AppendLine($"**{title}: {list.Count}**{(list.Count == 0 ? "" : ": " + string.Join(", ", list.Select(p => $"`{p.B.Id}` {p.B.Query} ({p.A.Status} → {p.B.Status})")))}");
        md.AppendLine();
    }

    public static IReadOnlyList<Row> Load(string dir)
    {
        var path = SummaryWriter.FindResults(dir) ?? throw new FileNotFoundException($"No results.jsonl in {dir} (or {dir}/raw)");
        return File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).Select(ParseRow)
            .Where(r => r.Status != Outcomes.Skipped).ToList();
    }

    private static Row ParseRow(JsonObject o)
    {
        var p = o["pass"]!.AsObject();
        var status = o["status"]!.GetValue<string>();
        var score = new CaseScore(
            p["answerable"]!.GetValue<bool>(),
            p["top1"]?.GetValue<bool>(),
            p["top5"]?.GetValue<bool>(),
            p["status"]?.GetValue<bool>(),
            status == Outcomes.Match,
            p["match_correct"]?.GetValue<bool>(),
            p["false_match"]!.GetValue<bool>(),
            p["target_rank"]?.GetValue<int>(),
            p["dup_record_match"]?.GetValue<bool>() ?? false);
        return new Row(o["id"]!.GetValue<string>(), o["query"]!.GetValue<string>(), o["category"]!.GetValue<string>(), status, score);
    }

    private static JsonObject? Meta(string dir)
    {
        var path = Path.Combine(dir, "summary.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
    }

    private static string Describe(JsonNode? n) => n switch
    {
        null => "n/a",
        JsonObject o when o["name"] is not null => $"{o["name"]} v{o["version"]} ({o["sha256"]})",
        JsonArray arr => arr.Count == 0 ? "none" : Fmt.Cell(string.Join("; ", arr.Select(x => x?.ToString()))),
        _ => Fmt.Cell(n.ToJsonString().Trim('"')),
    };
}
