using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Replay;

/// <summary>A recording to export, with the largest limit any request asked of it and the case ids that used it.</summary>
public sealed record FixtureEntry(RecordedResponse Response, int MaxLimit, IReadOnlyList<string> Cases);

/// <summary>
/// Writes a self-contained fixture folder (tests/fixtures/golden): one trimmed copy per recording that a run actually
/// used (docs cut to the largest limit requested, only the fields the pipeline reads) plus _manifest.json, which lists
/// every file with its request and origin and keeps a history of builds. Fixture files keep the {"url","dt","body"}
/// format, so the replay catalog reads them like the research caches.
/// </summary>
public static class FixtureWriter
{
    public const string ManifestName = "_manifest.json";

    private static readonly string[] DocFields =
    [
        "key", "title", "subtitle", "author_name", "author_key", "first_publish_year", "edition_count", "cover_i",
        "contributor", "readinglog_count", "author_alternative_name", "person",
    ];

    /// <param name="replace">Delete existing search_*.json first (a full rebuild); otherwise add/overwrite (merge).</param>
    public static int Write(string folder, IEnumerable<FixtureEntry> entries, bool replace, JsonObject build)
    {
        Directory.CreateDirectory(folder);
        var previous = ReadManifest(folder);
        if (replace)
            foreach (var old in Directory.EnumerateFiles(folder, "search_*.json")) File.Delete(old);

        var usedBy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var written = 0;
        foreach (var e in entries.OrderBy(e => e.Response.Url, StringComparer.Ordinal))
        {
            var name = RecordingHandler.FileName(e.Response.Url);
            var record = new JsonObject
            {
                ["url"] = e.Response.Url,
                ["dt"] = e.Response.Dt,
                ["source"] = e.Response.Origin ?? OriginOf(e.Response.File),
                ["recorded_at"] = e.Response.RecordedAt,
                ["body"] = Trim(e.Response, e.MaxLimit),
            };
            File.WriteAllText(Path.Combine(folder, name), record.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + "\n");
            usedBy[name] = e.Cases;
            written++;
        }

        WriteManifest(folder, previous, usedBy, build);
        return written;
    }

    private static JsonObject Trim(RecordedResponse r, int maxLimit)
    {
        var docs = new JsonArray();
        foreach (var d in r.Docs.Take(Math.Max(1, maxLimit)))
        {
            var src = JsonNode.Parse(d.GetRawText())!.AsObject();
            var kept = new JsonObject();
            foreach (var f in DocFields)
            {
                if (!src.TryGetPropertyValue(f, out var v) || v is null) continue;
                kept[f] = f == "author_alternative_name" && v is JsonArray alt
                    ? new JsonArray([.. alt.Take(SearchDocMapper.MaxAlternativeNames).Select(x => x?.DeepClone())])
                    : v.DeepClone();
            }
            if (src["editions"] is JsonObject eds && eds["docs"] is JsonArray edDocs)
            {
                var titles = new JsonArray([.. edDocs.Take(SearchDocMapper.MaxEditionTitles)
                    .Select(x => (JsonNode)new JsonObject { ["key"] = x?["key"]?.DeepClone(), ["title"] = x?["title"]?.DeepClone() })]);
                kept["editions"] = new JsonObject { ["numFound"] = eds["numFound"]?.DeepClone(), ["docs"] = titles };
            }
            docs.Add(kept);
        }
        return new JsonObject { ["numFound"] = r.NumFound, ["docs"] = docs };
    }

    /// <summary>Where a recording came from, without machine paths (e.g. "research archive/07-fix-misses/cache/…").</summary>
    private static string OriginOf(string file) => Reports.SummaryWriter.Display(file);

    private static JsonObject? ReadManifest(string folder)
    {
        var path = Path.Combine(folder, ManifestName);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
    }

    private static void WriteManifest(string folder, JsonObject? previous, IReadOnlyDictionary<string, IReadOnlyList<string>> usedBy, JsonObject build)
    {
        var oldUsedBy = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (previous?["files"] is JsonArray oldFiles)
            foreach (var f in oldFiles.OfType<JsonObject>())
                if (f["file"]?.GetValue<string>() is { } n) oldUsedBy[n] = f["used_by"]?.DeepClone();

        var files = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(folder, "search_*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (RecordedResponseIndex.TryRead(path, 0) is not { } r) continue;
            files.Add(new JsonObject
            {
                ["file"] = name,
                ["request"] = r.Request.Key.Text,
                ["limit"] = r.Request.Limit,
                ["docs"] = r.Docs.Count,
                ["source"] = r.Origin,
                ["has_v3_fields"] = r.Has("editions") || r.Has("editions.title") || r.Has("author_alternative_name"),
                ["used_by"] = usedBy.TryGetValue(name, out var cases)
                    ? new JsonArray([.. cases.Select(c => (JsonNode)c)])
                    : oldUsedBy.GetValueOrDefault(name),
            });
        }

        var builds = previous?["builds"] is JsonArray b ? (JsonArray)b.DeepClone() : [];
        builds.Add(build);
        var manifest = new JsonObject
        {
            ["what"] = "Recorded Open Library search.json responses for the golden replay regression test (tests/FindThatBook.Api.Tests/Golden). "
                       + "Built by the eval tool; never edited by hand. Replay matches q/title/author/author_key/sort and ignores fields/limit.",
            ["rebuild"] = "dotnet run --project tools/FindThatBook.Eval -- fixtures   (offline, from the replay sources)   |   "
                          + "dotnet run --project tools/FindThatBook.Eval -- record   (live Open Library, LLM off)",
            ["known_bias"] = "Files with has_v3_fields=false were recorded by the research prototypes before v3 asked for editions.title and "
                             + "author_alternative_name, so edition-title and alias-name evidence cannot fire on them. "
                             + "Re-record live to remove the bias.",
            ["file_count"] = files.Count,
            ["files"] = files,
            ["builds"] = builds,
        };
        File.WriteAllText(Path.Combine(folder, ManifestName), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
