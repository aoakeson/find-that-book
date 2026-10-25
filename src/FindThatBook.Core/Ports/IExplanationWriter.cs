using FindThatBook.Core.Explanations;

namespace FindThatBook.Core.Ports;

/// <summary>
/// One batched LLM call that turns validated facts into one sentence per work id. It never sees the raw query
/// (AI-29/AI-30). Output is untrusted: <c>ExplanationService</c> grounds it or falls back to the template.
/// </summary>
public interface IExplanationWriter
{
    Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct);
}
