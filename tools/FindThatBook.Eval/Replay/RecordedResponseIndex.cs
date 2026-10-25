using System.Text.Json;

namespace FindThatBook.Eval.Replay;

/// <summary>One recorded search.json response ({"url","dt","body"}: the research caches' format and our fixtures').</summary>
/// <param name="SourceRank">Position of its folder in the source list (lower = preferred).</param>
/// <param name="Origin">Where the response first came from (a research cache file or "live"), kept when fixtures are re-exported.</param>
public sealed record RecordedResponse(string Url, ParsedUrl Request, double? Dt, JsonElement Body, string File, int SourceRank,
    string? Origin = null, string? RecordedAt = null)
{
    public IReadOnlyList<JsonElement> Docs => Body.TryGetProperty("docs", out var d) && d.ValueKind == JsonValueKind.Array ? [.. d.EnumerateArray()] : [];

    public int? NumFound =>
        Body.TryGetProperty("numFound", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32()
        : Body.TryGetProperty("num_found", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32()
        : null;

    /// <summary>The recording holds every hit up to <paramref name="limit"/>: its limit was at least that, or it returned all hits.</summary>
    public bool Covers(int limit) => (Request.Limit ?? 100) >= limit || (NumFound is { } nf && Docs.Count >= nf);

    public bool Has(string field) => Request.Fields.Contains(field);
}

/// <summary>
/// Recorded responses from one or more folders, looked up by <see cref="RequestKey"/>. Exact (case-sensitive) parameter
/// matches win over case-insensitive ones; among equals the index prefers a recording that covers the requested limit,
/// carries person when it was asked for, has the v3 fields (editions, author_alternative_name), then the earlier folder.
/// </summary>
public sealed class RecordedResponseIndex
{
    private readonly Dictionary<string, List<RecordedResponse>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RecordedResponse>> _folded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public IReadOnlyList<(string Folder, int Files)> Sources { get; }
    public int Skipped { get; }

    public int Count
    {
        get { lock (_gate) return _files.Count; }
    }

    private RecordedResponseIndex(IReadOnlyList<RecordedResponse> responses, IReadOnlyList<(string, int)> sources, int skipped)
    {
        foreach (var r in responses) Add(r);
        Sources = sources;
        Skipped = skipped;
    }

    /// <summary>Adds a recording (the record-on-miss cache adds what it just fetched). A file already indexed is ignored.</summary>
    public void Add(RecordedResponse r)
    {
        lock (_gate)
        {
            if (!_files.Add(r.File)) return;
            Add(_exact, r.Request.Key.IndexKey, r);
            Add(_folded, r.Request.Key.IndexKey, r);
        }
    }

    public static RecordedResponseIndex Load(IEnumerable<string> folders)
    {
        var responses = new List<RecordedResponse>();
        var sources = new List<(string, int)>();
        var skipped = 0;
        var rank = 0;
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) { sources.Add((folder, 0)); rank++; continue; }
            var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
            var loaded = 0;
            foreach (var file in files)
            {
                if (TryRead(file, rank) is { } r) { responses.Add(r); loaded++; }
                else skipped++;
            }
            sources.Add((folder, loaded));
            rank++;
        }
        return new RecordedResponseIndex(responses, sources, skipped);
    }

    public static RecordedResponseIndex From(IEnumerable<RecordedResponse> responses) => new([.. responses], [], 0);

    /// <summary>Reads one {"url","dt","body"} file; null for anything else (manifests, author searches, error bodies).</summary>
    public static RecordedResponse? TryRead(string file, int sourceRank)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object
                || body.TryGetProperty("_error", out _)
                || !body.TryGetProperty("docs", out _))
                return null;
            if (RequestKey.Parse(url.GetString()!) is not { } parsed) return null;
            double? dt = root.TryGetProperty("dt", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
            return new RecordedResponse(url.GetString()!, parsed, dt, body.Clone(), file, sourceRank,
                OptionalString(root, "source"), OptionalString(root, "recorded_at"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public RecordedResponse? Find(RequestKey key, int limit, bool needPersons)
    {
        List<RecordedResponse>? candidates;
        lock (_gate)
        {
            candidates = _exact.TryGetValue(key.IndexKey, out var exact) ? [.. exact]
                : _folded.TryGetValue(key.IndexKey, out var folded) ? [.. folded]
                : null;
        }
        return candidates?
            .OrderByDescending(r => r.Covers(limit))
            .ThenByDescending(r => !needPersons || r.Has("person"))
            .ThenByDescending(r => r.Has("editions") || r.Has("editions.title"))
            .ThenByDescending(r => r.Has("author_alternative_name"))
            .ThenBy(r => r.SourceRank)
            .ThenBy(r => r.File, StringComparer.Ordinal)
            .First();
    }

    private static string? OptionalString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void Add(Dictionary<string, List<RecordedResponse>> map, string key, RecordedResponse r)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(r);
    }
}
