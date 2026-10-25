using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Interpretation;

/// <summary>
/// Policy around the LLM interpreter: sanitize its output, fall back to "no hypothesis" (the heuristic arm, the
/// measured LLM-off configuration) when it is unavailable, and say why. Never throws except caller cancellation.
/// </summary>
public sealed class InterpretationService(IQueryInterpreter interpreter)
{
    public async Task<InterpretationOutcome> InterpretAsync(CleanedQuery query, CancellationToken ct)
    {
        LlmResult<QueryHypothesis> result;
        try
        {
            result = await interpreter.InterpretAsync(query, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Adapters must not throw; if one does, the search still runs on the heuristic arm.
            var call = new LlmCall("interpret", "unknown", "unknown", "unknown", LlmCallStatus.ProviderError, false, null, null, 0, ex.GetType().Name);
            return new InterpretationOutcome(null, InterpretationSource.Heuristic, DegradedReason.InterpretationFallback, call);
        }

        if (result.Call.Status == LlmCallStatus.Ok && result.Value is { } raw)
            return new InterpretationOutcome(HypothesisSanitizer.Sanitize(raw, query), InterpretationSource.Llm, null, result.Call);

        var reason = result.Call.Status switch
        {
            LlmCallStatus.NotConfigured => DegradedReason.AiNotConfigured,
            LlmCallStatus.BudgetExhausted => DegradedReason.AiBudgetExhausted,
            _ => DegradedReason.InterpretationFallback,
        };
        return new InterpretationOutcome(null, InterpretationSource.Heuristic, reason, result.Call);
    }
}

/// <param name="Hypothesis">Null when the heuristic arm is used (LLM off, failed, or produced nothing usable).</param>
public sealed record InterpretationOutcome(QueryHypothesis? Hypothesis, InterpretationSource Source, DegradedReason? Degraded, LlmCall? Call);
