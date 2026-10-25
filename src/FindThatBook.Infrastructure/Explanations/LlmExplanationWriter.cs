using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Core.Explanations;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Explanations;

/// <summary>
/// One batched explain call (prompt explain.v5) over validated facts only. There is no "Search:" line and no raw
/// query (AI-29), so two queries with the same facts send byte-identical prompts and share one cache entry safely (AI-30).
/// The output is untrusted: <see cref="ExplanationService"/> grounds every sentence. Unknown, blank and repeated ids are
/// dropped here (AI-33).
/// </summary>
public sealed class LlmExplanationWriter(IStructuredLlm llm, IOptions<AiOptions> options) : IExplanationWriter
{
    public const string PromptId = "explain.v5";

    /// <summary>Deterministic and compact: declaration order, nulls omitted, non-Latin names unescaped (三体, not 三).</summary>
    private static readonly JsonSerializerOptions UserJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct)
    {
        var result = await llm.CallAsync<ExplainOutput>(BuildCall(facts, options.Value), ct).ConfigureAwait(false);
        return new LlmResult<IReadOnlyDictionary<string, string>>(result.Value is null ? null : Map(result.Value, facts), result.Call);
    }

    /// <summary>The exact request (shared with the smoke test).</summary>
    public static StructuredCall BuildCall(IReadOnlyList<ExplanationFacts> facts, AiOptions options) => new(
        Operation: "explain",
        PromptId: PromptId,
        SystemPrompt: PromptLibrary.Get(PromptId),
        UserContent: UserContent(facts),
        MaxOutputTokens: MaxOutputTokens(facts.Count, options),
        Timeout: options.Explain.Timeout);

    /// <summary>AI-34: 10 items used 455 of 600 tokens, so the cap grows with the batch (80 + 60 per item, at least the configured 600).</summary>
    public static int MaxOutputTokens(int items, AiOptions options) => Math.Max(options.Explain.MaxOutputTokens, 80 + 60 * items);

    public static string UserContent(IReadOnlyList<ExplanationFacts> facts) =>
        JsonSerializer.Serialize(new ExplainInput([.. facts.Select(ExplainInputItem.From)]), UserJson);

    /// <summary>One sentence per known id; the first answer for an id wins, and blank sentences count as missing.</summary>
    internal static IReadOnlyDictionary<string, string> Map(ExplainOutput output, IReadOnlyList<ExplanationFacts> facts)
    {
        var wanted = facts.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var sentences = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in output.Items ?? [])
        {
            var id = item?.Id?.Trim();
            var sentence = item?.Sentence?.Trim();
            if (id is null || !wanted.Contains(id) || string.IsNullOrEmpty(sentence)) continue;
            sentences.TryAdd(id, sentence);
        }
        return sentences;
    }
}

/// <summary>What the LLM sees per item: facts only, snake_case, no internal tier letters or field jargon (AI-32).</summary>
internal sealed record ExplainInput([property: JsonPropertyName("items")] IReadOnlyList<ExplainInputItem> Items);

internal sealed record ExplainInputItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("match_phrase")] string MatchPhrase,
    [property: JsonPropertyName("also_known_as")] string? AlsoKnownAs,
    [property: JsonPropertyName("requested_edition")] string? RequestedEdition,
    [property: JsonPropertyName("primary_authors")] IReadOnlyList<string> PrimaryAuthors,
    [property: JsonPropertyName("author_also_listed_as")] string? AuthorAlsoListedAs,
    [property: JsonPropertyName("contributors")] IReadOnlyList<ExplainContributor>? Contributors,
    [property: JsonPropertyName("matched_contributor")] string? MatchedContributor,
    [property: JsonPropertyName("matched_terms")] IReadOnlyList<string>? MatchedTerms,
    [property: JsonPropertyName("matched_characters")] IReadOnlyList<string>? MatchedCharacters,
    [property: JsonPropertyName("matched_subjects")] IReadOnlyList<string>? MatchedSubjects,
    [property: JsonPropertyName("first_publish_year")] int? FirstPublishYear,
    [property: JsonPropertyName("edition_count")] int EditionCount)
{
    public static ExplainInputItem From(ExplanationFacts f) => new(
        f.Id,
        f.Title,
        f.MatchPhrase,
        f.AlsoKnownAs is null ? null : $"{f.AlsoKnownAsLabel ?? "also published as"} {f.AlsoKnownAs}",
        f.Intent switch { EditionIntent.Adaptation => "adaptation", EditionIntent.StudyGuide => "study guide", _ => null },
        f.PrimaryAuthors,
        f.AuthorAlias?.Alias,
        f.Contributors.Count == 0 ? null : [.. f.Contributors.Select(c => new ExplainContributor(c.Name, c.Role))],
        f.MatchedContributor,
        // Title words explain only a partial or interpreted title, as in the template: for an exact title they add
        // nothing ("matched terms about and boy"), and beside "Author-only match" they read as a title claim.
        f.MatchedTerms.Count > 0 && f.Kind is MatchKind.NearTitle or MatchKind.PartialTitle or MatchKind.InferredTitle ? f.MatchedTerms : null,
        f.MatchedCharacters.Count == 0 ? null : f.MatchedCharacters,
        f.MatchedSubjects.Count == 0 ? null : f.MatchedSubjects,
        f.FirstPublishYear,
        f.EditionCount);
}

internal sealed record ExplainContributor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role);

/// <summary>Structured output: {"items":[{"id","sentence"}]}. Every field required, no defaults (AI-07).</summary>
public sealed record ExplainOutput([property: JsonPropertyName("items")] ExplainOutputItem[] Items);

public sealed record ExplainOutputItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sentence")] string Sentence);
