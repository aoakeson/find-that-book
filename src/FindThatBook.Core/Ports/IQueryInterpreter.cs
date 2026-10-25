using FindThatBook.Core.Model;

namespace FindThatBook.Core.Ports;

/// <summary>LLM field extraction: messy text → search hypothesis. The adapter owns its timeout, cache and budget checks.</summary>
public interface IQueryInterpreter
{
    Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct);
}
