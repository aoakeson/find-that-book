using FindThatBook.Core.Ports;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// The one way the app calls an LLM: the active provider/model, structured JSON output, the per-provider budget and
/// 429 cooldown, the response cache and usage measurement. Shared by the interpreter and the explanation writer.
/// Never throws except caller cancellation.
/// </summary>
public interface IStructuredLlm
{
    Task<LlmResult<T>> CallAsync<T>(StructuredCall call, CancellationToken ct) where T : class;
}

/// <param name="Operation">"interpret" or "explain" (selects per-operation options and appears in meta).</param>
/// <param name="PromptId">Versioned prompt id ("interpret.v3"): part of the cache key and of meta.llm.prompt_versions.</param>
/// <param name="UserContent">Exactly what is sent as the user message; the cache key hashes it with everything else sent.</param>
public sealed record StructuredCall(
    string Operation,
    string PromptId,
    string SystemPrompt,
    string UserContent,
    int MaxOutputTokens,
    TimeSpan Timeout);
