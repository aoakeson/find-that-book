using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Messy text → <see cref="QueryHypothesis"/> with prompt interpret.v5: v3 (the measured v1 instructions, the English and
/// original title fields, a "data, not instructions" line) plus intent, subject headings and suggested books, where
/// "topic" is reserved for requests for books in general (v4 also labelled bare author and character names as topics),
/// with four contrast examples that are not in any eval set. The raw validated text goes in as a JSON string, so user text cannot
/// close a delimiter (AI-16) and non-Latin scripts reach the model unfolded (AI-24). Sanitizing is Core's job
/// (<c>InterpretationService</c>); this adapter only maps the output record.
/// </summary>
public sealed class LlmQueryInterpreter(IStructuredLlm llm, IOptions<AiOptions> options) : IQueryInterpreter
{
    public const string PromptId = "interpret.v5";

    private static readonly JsonSerializerOptions UserJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct)
    {
        var result = await llm.CallAsync<InterpretOutput>(BuildCall(query.Raw, options.Value), ct);
        return new LlmResult<QueryHypothesis>(result.Value?.ToHypothesis(), result.Call);
    }

    /// <summary>The exact request (shared with the admin probe and the smoke test).</summary>
    public static StructuredCall BuildCall(string rawQuery, AiOptions options) => new(
        Operation: "interpret",
        PromptId: PromptId,
        SystemPrompt: PromptLibrary.Get(PromptId),
        UserContent: UserContent(rawQuery),
        MaxOutputTokens: options.Interpret.MaxOutputTokens,
        Timeout: options.Interpret.Timeout);

    /// <summary>`"tolkien hobbit"`: the query as one JSON string literal, the input form prompt v3 was measured with.</summary>
    public static string UserContent(string rawQuery) => JsonSerializer.Serialize(rawQuery, UserJson);
}

/// <summary>
/// The structured output schema (prompt v4 fields). Every field required, no defaults: a default value drops the field
/// from "required" in M.E.AI's schema (AI-07); only strings, string arrays and one array of flat objects (AI-08).
/// Intent is a plain string, not an enum, so an unexpected value degrades to "book" instead of failing the parse.
/// </summary>
public sealed record InterpretOutput(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("characters")] string[] Characters,
    [property: JsonPropertyName("keywords")] string[] Keywords,
    [property: JsonPropertyName("english_title")] string? EnglishTitle,
    [property: JsonPropertyName("original_title")] string? OriginalTitle,
    [property: JsonPropertyName("intent")] string? Intent,
    [property: JsonPropertyName("subjects")] string[] Subjects,
    [property: JsonPropertyName("suggestions")] SuggestionOutput[] Suggestions)
{
    public QueryHypothesis ToHypothesis() =>
        new(Title, Author, Characters ?? [], Keywords ?? [], EnglishTitle, OriginalTitle)
        {
            Intent = string.Equals(Intent?.Trim(), "topic", StringComparison.OrdinalIgnoreCase) ? QueryIntent.Topic : QueryIntent.Book,
            Subjects = Subjects ?? [],
            Suggestions = [.. (Suggestions ?? []).OfType<SuggestionOutput>().Select(s => new BookSuggestion(s.Title ?? "", s.Author))],
        };
}

public sealed record SuggestionOutput(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("author")] string? Author);
