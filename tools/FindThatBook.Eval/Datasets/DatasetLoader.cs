using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Datasets;

/// <summary>
/// Loads the golden set (v1/v2 format), the sealed held-out set and jsonl sets (gen_queries.py / research hard set format).
/// The held-out set is refused unless the caller passes <c>allowHeldOut</c> (the CLI's --heldout flag).
/// </summary>
public static class DatasetLoader
{
    /// <summary>Short names for the datasets in eval/. Anything else is a path.</summary>
    public static IReadOnlyDictionary<string, string> Known { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["golden"] = "eval/golden.v2.json",
        ["golden-v2"] = "eval/golden.v2.json",
        ["golden-v1"] = "eval/golden.json",
        ["heldout"] = "eval/heldout.json",
        ["hard"] = "eval/datasets/hard.v1.jsonl",
        ["synthetic"] = "eval/datasets/synthetic-mirror.v1.jsonl",
        ["topic"] = "eval/datasets/topic.v2.jsonl",
        ["topic-v1"] = "eval/datasets/topic.v1.jsonl",
    };

    public const string BattleLabel = "synthetic adversarial, local mirror (no editions.title/alt names/person:), relative comparisons only";

    public static Dataset Load(string setOrPath, bool allowHeldOut)
    {
        if (setOrPath.Equals("battle", StringComparison.OrdinalIgnoreCase)) return LoadBattle(["r1", "r2", "r3"]);
        if (setOrPath.StartsWith("battle:", StringComparison.OrdinalIgnoreCase))
            return LoadBattle(setOrPath[7..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var path = Resolve(setOrPath);
        var bytes = File.ReadAllBytes(path);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var dataset = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonl(text, path, sha)
            : ParseJson(text, path, sha);
        if (dataset.HeldOut && !allowHeldOut) throw new HeldOutRefusedException(RepoPaths.Relative(path));
        return dataset;
    }

    public static string Resolve(string setOrPath)
    {
        if (Known.TryGetValue(setOrPath, out var known)) return Path.Combine(RepoPaths.Root, known);
        if (File.Exists(setOrPath)) return Path.GetFullPath(setOrPath);
        var underRoot = Path.Combine(RepoPaths.Root, setOrPath);
        if (File.Exists(underRoot)) return underRoot;
        throw new FileNotFoundException($"Unknown set '{setOrPath}' (known: {string.Join(", ", Known.Keys)}, or a .json/.jsonl path).");
    }

    public static Dataset ParseJson(string text, string path, string sha)
    {
        var root = JsonNode.Parse(text)!.AsObject();
        var heldOut = root["HELD_OUT"]?.GetValue<bool>() == true;
        var name = root["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
        var version = root["version"]?.GetValue<int>() ?? 1;
        var cases = root["cases"]!.AsArray().Select(c => heldOut ? HeldOutCase(c!.AsObject()) : GoldenCase(c!.AsObject())).ToList();
        var tuning = heldOut
            ? "HELD-OUT (sealed): never tuned on; final evaluation only"
            : root["tuning"]?["status"]?.GetValue<string>() is { } status
                ? $"{status}: {root["tuning"]?["detail"]?.GetValue<string>()}"
                : "in_sample (golden v1 has no tuning block; all rows were used while the research rules were tuned)";
        var label = heldOut ? "HELD-OUT RESULTS: sealed set, final evaluation only. Do not tune anything on these rows." : null;
        return new Dataset(name, version, path, sha, heldOut, tuning, label, cases);
    }

    private static EvalCase GoldenCase(JsonObject c)
    {
        var e = c["expected"]!.AsObject();
        var statuses = Strings(e["status"]);
        var top1 = Strings(e["top1"]);
        var expectation = new Expectation
        {
            Statuses = statuses,
            ErrorCode = e["error_code"]?.GetValue<string>(),
            HttpStatus = e["http_status"]?.GetValue<int>(),
            Top1 = top1,
            Top5 = e["top5"] is null ? top1 : Strings(e["top5"]),
            AuthorKeys = Strings(e["author_keys"]),
            AuthorSurname = e["author_surname"]?.GetValue<string>(),
            Describe = e["author"]?.GetValue<string>() is { } author
                ? $"author: {author}"
                : c["canonical"]?["title"]?.GetValue<string>(),
        };
        // golden v1 has no per-row flag; every golden row was in-sample for the research rules.
        var inSample = c["in_sample"]?.GetValue<bool>() ?? true;
        return new EvalCase(c["id"]!.GetValue<string>(), c["query"]!.GetValue<string>(), c["category"]!.GetValue<string>(), expectation, inSample);
    }

    private static EvalCase HeldOutCase(JsonObject c)
    {
        var kind = c["kind"]!.GetValue<string>();
        var ids = Strings(c["target_work_ids"]);
        var expectation = new Expectation
        {
            Statuses = Strings(c["acceptable_status"]),
            Top1 = kind == "author" ? [] : ids,
            Top5 = kind == "author" ? [] : ids,
            AuthorNames = kind == "author" ? Strings(c["target_authors"]) : [],
            WrongMatchIsStatusError = true,
            Describe = c["target_title"]?.GetValue<string>() ?? kind,
        };
        return new EvalCase(c["id"]!.GetValue<string>(), c["query"]!.GetValue<string>(), c["style"]?.GetValue<string>() ?? kind, expectation, false);
    }

    public static Dataset ParseJsonl(string text, string path, string sha)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        JsonObject? meta = null;
        var cases = new List<EvalCase>();
        var stem = Path.GetFileNameWithoutExtension(path);
        foreach (var line in lines)
        {
            var row = JsonNode.Parse(line)!.AsObject();
            if (row["_meta"] is JsonObject m) { meta = m; continue; }
            // A battle-round row (resolved.jsonl shape: qid + accept) is scored like --set battle.
            cases.Add(row["qid"] is not null && row["accept"] is not null
                ? BattleCase(row)
                : JsonlCase(row, $"{stem}-{cases.Count + 1:0000}", inSample: meta?["tuning"]?.GetValue<string>()?.StartsWith("in_sample", StringComparison.Ordinal) == true));
        }
        var heldOut = meta?["held_out"]?.GetValue<bool>() == true;
        return new Dataset(
            meta?["name"]?.GetValue<string>() ?? stem,
            meta?["version"]?.GetValue<int>() ?? 1,
            path, sha, heldOut,
            meta?["tuning"]?.GetValue<string>() ?? "unknown (no _meta line)",
            meta?["label"]?.GetValue<string>(),
            cases);
    }

    private static EvalCase JsonlCase(JsonObject r, string fallbackId, bool inSample)
    {
        var kind = r["kind"]?.GetValue<string>() ?? "unknown";
        var work = r["expected_work"]?.GetValue<string>();
        var author = r["expected_author"]?.GetValue<string>() is { Length: > 0 } a ? a : null;
        var title = r["expected_title"]?.GetValue<string>() is { Length: > 0 } t ? t : null;
        var statuses = r["expected_status"] is { } st ? (st is JsonArray ? Strings(st) : [st.GetValue<string>()]) : [];
        var category = r["category"]?.GetValue<string>() ?? kind;
        if (kind == "topic")
        {
            var topic = new Expectation
            {
                Statuses = statuses,
                ExpectedReason = r["expected_reason"]?.GetValue<string>(),
                TopicWords = Strings(r["topic_words"]),
                MinTopicHits = r["min_topic_hits"]?.GetValue<int>() ?? 3,
                Describe = $"topic: {string.Join(", ", Strings(r["topic_words"]))}",
            };
            return new EvalCase(r["id"]?.GetValue<string>() ?? fallbackId, r["query"]!.GetValue<string>(), category, topic, inSample);
        }
        var authorRow = kind == "author_only" || work is null;
        var strict = r["strict"]?.GetValue<bool>() == true; // golden-style: only the listed ids count
        var ids = work is null ? [] : new[] { work }.Concat(Strings(r["accept"])).ToList();
        var expectation = new Expectation
        {
            Statuses = statuses,
            Top1 = authorRow ? [] : ids,
            Top5 = authorRow ? [] : ids,
            AuthorNames = authorRow && author is not null ? [author] : [],
            LenientTitle = authorRow || strict ? null : title,
            LenientAuthor = authorRow || strict ? null : author,
            Describe = authorRow ? $"author: {author}" : $"{title} / {author}",
        };
        return new EvalCase(r["id"]?.GetValue<string>() ?? fallbackId, r["query"]!.GetValue<string>(), category, expectation, inSample);
    }

    /// <summary>
    /// The battle-test rounds (11-battle-test/rounds/rN/resolved.jsonl in the optional research archive, read in place;
    /// not part of this repo; eval/datasets/battle-live-sample.v1.jsonl is a 200-row copy that is), scored like judge.py: book rows accept the target and its split records (a folded duplicate counts), author
    /// rows accept any work by an accepted author key, none rows score status only. Expected status follows judge.py:
    /// "match" rows must match; "ambiguous" rows also accept a match except in the categories judge.py calls status_over.
    /// In-sample: the v3 fixes and the R3-xxxx ranker rules were designed on these rounds.
    /// </summary>
    public static Dataset LoadBattle(IReadOnlyList<string> rounds)
    {
        var cases = new List<EvalCase>();
        var hash = new List<byte>();
        var paths = new List<string>();
        foreach (var round in rounds)
        {
            if (round is not ("r1" or "r2" or "r3")) throw new ArgumentException($"Unknown battle round '{round}' (r1, r2, r3).");
            var path = Path.Combine(RepoPaths.Research, "11-battle-test", "rounds", round, "resolved.jsonl");
            if (!File.Exists(path))
                throw new FileNotFoundException($"--set battle needs the research archive (not part of this repo): {path} not found; pass --research <dir>.");
            var bytes = File.ReadAllBytes(path);
            hash.AddRange(bytes);
            paths.Add(path);
            foreach (var line in System.Text.Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                cases.Add(BattleCase(JsonNode.Parse(line)!.AsObject()));
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData([.. hash]))[..16];
        var name = rounds.Count == 3 ? "battle rounds r1-r3" : $"battle rounds {string.Join("+", rounds)}";
        return new Dataset(name, 1, paths.Count == 1 ? paths[0] : Path.GetDirectoryName(Path.GetDirectoryName(paths[0]))!, sha, false,
            "in_sample: battle rounds from the research archive; the v3 fixes and the ranker's R3-xxxx rules were designed on them",
            BattleLabel, cases) { Slug = "battle-" + string.Join("-", rounds) };
    }

    private static readonly HashSet<string> StatusOverCategories = ["ambiguous_title", "author_wrong_title", "author_only"];

    private static EvalCase BattleCase(JsonObject r)
    {
        var kind = r["kind"]!.GetValue<string>();
        var expect = r["expect"]!.GetValue<string>();
        var category = r["category"]?.GetValue<string>() ?? kind;
        var accept = Strings(r["accept"]);
        Expectation e;
        if (kind == "none" || (expect == "none" && kind != "author"))
            e = new Expectation { Statuses = [Outcomes.None], Describe = "no such book" };
        else if (kind == "author")
            e = new Expectation
            {
                Statuses = [Outcomes.Ambiguous],
                AuthorKeys = Strings(r["author_accept"]),
                Describe = $"author: {r["author"]?.GetValue<string>()}",
            };
        else
            e = new Expectation
            {
                Statuses = expect == "match" ? [Outcomes.Match]
                    : StatusOverCategories.Contains(category) ? [Outcomes.Ambiguous] : [Outcomes.Ambiguous, Outcomes.Match],
                Top1 = accept,
                Top5 = accept,
                CountFolded = true,
                DupTitles = [.. new[] { r["title"]?.GetValue<string>(), r["target_title"]?.GetValue<string>() }.OfType<string>(), .. Strings(r["alt_titles"])],
                DupAuthors = [.. new[] { r["author"]?.GetValue<string>() }.OfType<string>(), .. Strings(r["author_alt"]), .. Strings(r["target_authors"])],
                Describe = $"{r["target_title"]?.GetValue<string>()} / {string.Join(", ", Strings(r["target_authors"]))}",
            };
        return new EvalCase(r["qid"]!.GetValue<string>(), r["query"]!.GetValue<string>(), category, e, InSample: true);
    }

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray a ? a.Where(x => x is not null).Select(x => x!.GetValue<string>()).ToList() : [];
}
