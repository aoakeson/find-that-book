using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FindThatBook.Core.Model;
using FindThatBook.Core.Search;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// Real recorded Open Library pools (research caches plus a few live fetches, docs trimmed to the fields the ranker reads)
/// with the measured reference ranker's result for each case. Test-only loader: Core has no JSON DTOs. Files sit next
/// to this source file (found via CallerFilePath).
/// </summary>
internal static class PoolFixtures
{
    public const string GoldenHard = "golden-hard.json";
    public const string Targeted = "targeted.json";
    public const string Topic = "topic.json";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, PoolCase>> Files = new();

    public static PoolCase Case(string id)
        => (Load(Topic).TryGetValue(id, out var topic) ? topic : null)
           ?? (Load(Targeted).TryGetValue(id, out var c) ? c : null) ?? Load(GoldenHard)[id];

    public static IReadOnlyDictionary<string, PoolCase> Load(string file)
        => Files.GetOrAdd(file, f => Parse(Path.Combine(Dir(), "Pools", f)));

    public static TheoryData<string> Ids(params string[] files)
    {
        var data = new TheoryData<string>();
        foreach (var f in files)
            foreach (var id in Load(f).Keys) data.Add(id);
        return data;
    }

    /// <summary>The reference ranker's step names → <see cref="Retrieval.CatalogSteps"/>.</summary>
    public static string Step(string recorded) => recorded switch
    {
        "q" => "broad",
        "hyp" => "hypothesis",
        "hyp_orig" => "original_title",
        "person" => "person",
        "person_or" => "person_any",
        "or" => "any_of",
        "person_fb" => "person_fallback",
        "person_fb_or" => "person_fallback_any",
        "author" => "author_works",
        _ => recorded,
    };

    private static string Dir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static IReadOnlyDictionary<string, PoolCase> Parse(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var works = root.GetProperty("docs").EnumerateArray().Select(Work).ToList();
        var cases = new Dictionary<string, PoolCase>(StringComparer.Ordinal);
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            cases[id] = new PoolCase(
                id,
                c.GetProperty("query").GetString()!,
                Hypothesis(c.GetProperty("hypothesis")),
                Calls(c, "calls", works),
                Calls(c, "extra_calls", works),
                c.TryGetProperty("reference", out var reference) ? Reference(reference) : ReferenceResult.None)
            {
                Expected = c.TryGetProperty("expected", out var e) ? Expected(e) : null,
                Cleaned = c.TryGetProperty("cleaned", out var cleaned) ? Cleaned(cleaned) : null,
            };
        }
        return cases;
    }

    private static IReadOnlyList<PoolCall> Calls(JsonElement c, string name, List<CandidateWork> works)
        => c.TryGetProperty(name, out var calls)
            ? [.. calls.EnumerateArray().Select(call => new PoolCall(
                Step(call.GetProperty("step").GetString()!),
                call.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString()),
                [.. call.GetProperty("docs").EnumerateArray().Select(i => works[i.GetInt32()])]))]
            : [];

    private static CandidateWork Work(JsonElement d)
    {
        int? Int(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        return new CandidateWork
        {
            WorkId = d.GetProperty("key").GetString()!.Split('/')[^1],
            Title = Str(d, "title") ?? "",
            Subtitle = Str(d, "subtitle"),
            AuthorNames = Strings(d, "author_name"),
            AuthorKeys = Strings(d, "author_key"),
            Contributors = Strings(d, "contributor"),
            FirstPublishYear = Int("first_publish_year") is > 0 and var y ? y : null,
            EditionCount = Int("edition_count") ?? 0,
            CoverId = Int("cover_i") is > 0 and var cover ? cover : null,
            ReadingLogCount = Int("readinglog_count") ?? 0,
            EditionTitles = Strings(d, "edition_titles"),
            AuthorAlternativeNames = Strings(d, "author_alternative_name"),
            Persons = Strings(d, "person"),
            Subjects = Strings(d, "subject"),
            EditionKey = Str(d, "edition_key"),
        };
    }

    private static CleanedSpec Cleaned(JsonElement e)
        => new(Strings(e, "search"), Strings(e, "request"), Strings(e, "topic"), Str(e, "topic_author"));

    private static Expectation Expected(JsonElement e)
    {
        var status = Strings(e, "status").Select(s => s switch { "match" => MatchStatus.Match, "none" => MatchStatus.None, _ => MatchStatus.Ambiguous });
        return new Expectation([.. status], Strings(e, "top1"), Strings(e, "author_keys"), Str(e, "expected_work"), Strings(e, "accept"),
            Str(e, "expected_title"), Str(e, "expected_author"), Str(e, "kind"));
    }

    private static QueryHypothesis? Hypothesis(JsonElement h)
        => h.ValueKind != JsonValueKind.Object ? null
            : new QueryHypothesis(Str(h, "title"), Str(h, "author"), Strings(h, "characters"), Strings(h, "keywords"),
                Str(h, "english_title"), Str(h, "original_title"))
            {
                Intent = Str(h, "intent") == "topic" ? QueryIntent.Topic : QueryIntent.Book,
                Subjects = Strings(h, "subjects"),
                Suggestions = h.TryGetProperty("suggestions", out var s) && s.ValueKind == JsonValueKind.Array
                    ? [.. s.EnumerateArray().Select(x => new BookSuggestion(Str(x, "title") ?? "", Str(x, "author")))]
                    : [],
            };

    private static ReferenceResult Reference(JsonElement p)
    {
        var cq = p.GetProperty("cq");
        return new ReferenceResult(
            p.GetProperty("status").GetString() switch { "match" => MatchStatus.Match, "none" => MatchStatus.None, _ => MatchStatus.Ambiguous },
            [.. p.GetProperty("ranked").EnumerateArray().Select(r => new ReferenceRow(
                r.GetProperty("id").GetString()!,
                Enum.Parse<MatchTier>(r.GetProperty("tier").GetString()!),
                r.GetProperty("score").GetDouble(),
                r.GetProperty("exact").GetBoolean(),
                Str(r, "role"),
                r.GetProperty("intent_hit").GetBoolean(),
                Strings(r, "dups"),
                r.GetProperty("foreign").GetBoolean()))],
            p.GetProperty("n").GetInt32(),
            [.. Strings(p, "steps").Select(Step)],
            new ReferenceQuery(Strings(cq, "all"), Strings(cq, "search"), Strings(cq, "optional"),
                cq.GetProperty("year").ValueKind == JsonValueKind.Number ? cq.GetProperty("year").GetInt32() : null,
                Strings(cq, "head"), Strings(cq, "intents"), Str(cq, "text") ?? ""),
            p.GetProperty("author_only").GetBoolean(),
            p.GetProperty("strong").GetBoolean(),
            Str(p, "pick_author"));
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IReadOnlyList<string> Strings(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];
}

internal sealed record PoolCall(string Step, IReadOnlyDictionary<string, string> Params, IReadOnlyList<CandidateWork> Works);

internal sealed record PoolCase(string Id, string Query, QueryHypothesis? Hypothesis, IReadOnlyList<PoolCall> Calls,
    IReadOnlyList<PoolCall> ExtraCalls, ReferenceResult Reference)
{
    /// <summary>Golden (v1) and hard-set expectations; null for targeted cases.</summary>
    public Expectation? Expected { get; init; }

    /// <summary>Topic cases: the cleaner output the feature specifies for the query.</summary>
    public CleanedSpec? Cleaned { get; init; }

    /// <summary>The recorded pool: every call merged in step order, first sighting wins (as SearchService merges).</summary>
    public IReadOnlyList<CandidateWork> Pool()
    {
        var pool = new CandidatePool();
        foreach (var call in Calls) pool.Add(call.Works);
        return [.. pool.Works];
    }

    /// <summary>The reference ranker's cleaned query, so ranking parity does not depend on the C# cleaner.</summary>
    public CleanedQuery ReferenceCleanedQuery()
    {
        var q = Reference.Cq;
        return new CleanedQuery
        {
            Raw = Query,
            Text = q.Text,
            All = q.All,
            Search = q.Search,
            Optional = q.Optional.ToHashSet(StringComparer.Ordinal),
            Head = q.Head,
            Year = q.Year,
            SoftTokens = new HashSet<string>(),
            Intents = q.Intents.Select(i => i switch
            {
                "adapt" => EditionIntent.Adaptation,
                "study" => EditionIntent.StudyGuide,
                _ => EditionIntent.Omnibus,
            }).ToHashSet(),
            RemovedNoise = [],
        };
    }

    public override string ToString() => Id;
}

internal sealed record ReferenceRow(string Id, MatchTier Tier, double Score, bool Exact, string? Role, bool IntentHit,
    IReadOnlyList<string> Dups, bool Foreign);

internal sealed record ReferenceQuery(IReadOnlyList<string> All, IReadOnlyList<string> Search, IReadOnlyList<string> Optional,
    int? Year, IReadOnlyList<string> Head, IReadOnlyList<string> Intents, string Text);

internal sealed record ReferenceResult(MatchStatus Status, IReadOnlyList<ReferenceRow> Ranked, int Count, IReadOnlyList<string> Steps,
    ReferenceQuery Cq, bool AuthorOnly, bool Strong, string? PickAuthor)
{
    /// <summary>Cases recorded without a reference result (topic requests are not part of the measured reference).</summary>
    public static ReferenceResult None { get; } = new(MatchStatus.None, [], 0, [], new ReferenceQuery([], [], [], null, [], [], ""), false, false, null);
}

internal sealed record CleanedSpec(IReadOnlyList<string> Search, IReadOnlyList<string> Request, IReadOnlyList<string> Topic, string? TopicAuthor);

/// <param name="Status">Golden: accepted statuses.</param>
/// <param name="Top1">Golden: accepted top-1 ids.</param>
/// <param name="AuthorKeys">Golden author-only cases: the top work must carry one of these keys.</param>
/// <param name="Work">Hard: the expected work (plus <paramref name="Accept"/>), or null for author-only kinds.</param>
internal sealed record Expectation(IReadOnlyList<MatchStatus> Status, IReadOnlyList<string> Top1, IReadOnlyList<string> AuthorKeys,
    string? Work, IReadOnlyList<string> Accept, string? Title, string? Author, string? Kind);
