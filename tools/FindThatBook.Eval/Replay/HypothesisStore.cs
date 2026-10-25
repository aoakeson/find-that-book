using System.Text.Json.Nodes;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Replay;

/// <summary>One recorded interpretation: the hypothesis (null when the call failed) and the call as it was measured.</summary>
public sealed record RecordedHypothesis(QueryHypothesis? Hypothesis, LlmCallStatus Status, string Model, string PromptId, string Source);

/// <summary>
/// Interpretations keyed by query, loaded from an eval results.jsonl (meta.hypothesis + the interpret LlmCall) or from a
/// research cache line {"query", "hyp"} (the optional research archive's 12-pipeline-v3/cache/hyp_v3.jsonl and
/// 11-battle-test/cache/hyp_v3.jsonl: OpenAI Codex as the LLM, prompt v3). Keys are the validated query text, as the pipeline sees it.
/// </summary>
public sealed class HypothesisStore
{
    private readonly Dictionary<string, RecordedHypothesis> _byQuery = new(StringComparer.Ordinal);

    public string Path { get; }
    public int Count => _byQuery.Count;

    private HypothesisStore(string path) => Path = path;

    public static HypothesisStore Load(string path)
    {
        var store = new HypothesisStore(System.IO.Path.GetFullPath(path));
        var researchModel = path.Contains("hyp_v3", StringComparison.Ordinal) ? "codex (research cache, prompt v3)"
            : path.Contains("hyp_v2", StringComparison.Ordinal) ? "codex (research cache, prompt v2)"
            : path.Contains("hyp_v1", StringComparison.Ordinal) ? "codex (research cache, prompt v1)"
            : "research cache";
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject row || row["query"]?.GetValue<string>() is not { } query) continue;
            var entry = row["meta"] is JsonObject meta ? FromResultRow(meta, store.Path) : FromResearch(row, researchModel, store.Path);
            if (entry is not null) store._byQuery[Key(query)] = entry;
        }
        return store;
    }

    public RecordedHypothesis? Find(string query) => _byQuery.GetValueOrDefault(Key(query));

    /// <summary>The pipeline's view of a query: QueryValidator's normalized text, inner whitespace collapsed.</summary>
    public static string Key(string query)
    {
        var text = QueryValidator.Validate(query) is { IsValid: true, Query: { } q } ? q : query;
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static RecordedHypothesis? FromResultRow(JsonObject meta, string source)
    {
        // A row that never reached the interpreter (rejected, error, skipped) has no interpret call: not an entry.
        var call = (meta["llm_calls"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(c => c["operation"]?.GetValue<string>() == "interpret");
        if (call is null) return null;
        var status = Enum.TryParse<LlmCallStatus>(call["status"]?.GetValue<string>()?.Replace("_", ""), ignoreCase: true, out var s) ? s : LlmCallStatus.ProviderError;
        var hypothesis = meta["hypothesis"] is JsonObject h ? Parse(h) : null;
        var model = $"{call["provider"]?.GetValue<string>()}/{call["model"]?.GetValue<string>()}";
        return new RecordedHypothesis(hypothesis, hypothesis is null ? status : LlmCallStatus.Ok, model, call["prompt_id"]?.GetValue<string>() ?? "unknown", source);
    }

    private static RecordedHypothesis FromResearch(JsonObject row, string model, string source) =>
        row["hyp"] is JsonObject h
            ? new RecordedHypothesis(Parse(h), LlmCallStatus.Ok, model, "interpret.v3", source)
            : new RecordedHypothesis(null, LlmCallStatus.MalformedOutput, model, "interpret.v3", source);

    private static QueryHypothesis Parse(JsonObject h) => new(
        Str(h["title"]), Str(h["author"]), Strs(h["characters"]), Strs(h["keywords"]), Str(h["english_title"]), Str(h["original_title"]));

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static IReadOnlyList<string> Strs(JsonNode? n) =>
        n is JsonArray a ? [.. a.Select(Str).OfType<string>()] : [];
}

/// <summary>
/// <see cref="IQueryInterpreter"/> that answers from a <see cref="HypothesisStore"/> instead of calling an LLM, so judge
/// on/off, ranker ablations and re-scoring reuse one LLM run. The pipeline's InterpretationService still sanitizes every
/// answer (research caches hold raw model output). A query with no entry is reported as hypothesis_miss and runs as
/// LLM-off (NotConfigured → heuristic arm).
/// </summary>
/// <remarks>With a <c>fallback</c> (--hypotheses-fill live) a miss is interpreted live by the app's own interpreter instead,
/// so a set that shares queries with an earlier run of the same build spends LLM calls only on its new queries.</remarks>
public sealed class ReplayQueryInterpreter(HypothesisStore store, Func<IQueryInterpreter>? fallback = null) : IQueryInterpreter
{
    public const string MissDetail = "hypothesis_miss";

    public Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (store.Find(query.Raw) is not { } hit)
        {
            if (fallback is not null) return fallback().InterpretAsync(query, ct);
            EvalContext.Current?.HypothesisMisses.Enqueue(query.Raw);
            return Task.FromResult(new LlmResult<QueryHypothesis>(null,
                new LlmCall("interpret", "replay", "none", "none", LlmCallStatus.NotConfigured, false, null, null, 0, MissDetail)));
        }
        // CacheHit = true: nothing was sent, so nothing is billed or counted against a budget.
        var call = new LlmCall("interpret", "replay", hit.Model, hit.PromptId, hit.Status, true, null, null, 0, "replay");
        return Task.FromResult(new LlmResult<QueryHypothesis>(hit.Hypothesis, call));
    }
}

/// <summary>--explain template: never calls the explain LLM; every shown item gets the code template (ranking is unaffected).</summary>
public sealed class TemplateOnlyExplanationWriter : IExplanationWriter
{
    public const string Detail = "explain_disabled_by_eval";

    public Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<Core.Explanations.ExplanationFacts> facts, CancellationToken ct) =>
        Task.FromResult(new LlmResult<IReadOnlyDictionary<string, string>>(null,
            new LlmCall("explain", "eval", "template", "template", LlmCallStatus.NotConfigured, false, null, null, 0, Detail)));
}
