using System.Diagnostics;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Explanations;

/// <summary>
/// Facts → one batched LLM call → grounding check per sentence → template fallback (AI-28..AI-34).
/// A missing or rejected sentence gets the template by design (not degraded); a failed or unavailable call gets
/// templates for every item and <see cref="DegradedReason.ExplanationFallback"/>. Never throws except caller cancellation.
/// </summary>
public sealed class ExplanationService(IExplanationWriter writer)
{
    private readonly IExplanationWriter _writer = writer;

    public async Task<ExplanationBatch> ExplainAsync(IReadOnlyList<RankedWork> works, CleanedQuery query, QueryHypothesis? hypothesis, CancellationToken ct)
    {
        var facts = ExplanationFactsBuilder.Build(works, query, hypothesis);
        if (facts.Count == 0) return new ExplanationBatch(new Dictionary<string, Explanation>(StringComparer.Ordinal), null, null);

        LlmResult<IReadOnlyDictionary<string, string>> result;
        var started = Stopwatch.GetTimestamp();
        try
        {
            result = await _writer.WriteAsync(facts, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Adapters report failures in LlmResult; a throw is a bug before or around the call (e.g. a missing embedded
            // prompt). The user still gets templates, and the failed step is recorded so meta and logs show it.
            var status = ex is OperationCanceledException ? LlmCallStatus.Timeout : LlmCallStatus.ProviderError;
            result = new(null, new LlmCall("explain", "unknown", "unknown", "unknown", status, false, null, null,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name));
        }

        var sentences = result.Value;
        var byId = new Dictionary<string, Explanation>(StringComparer.Ordinal);
        var rejected = new List<GroundingRejection>();
        foreach (var f in facts)
        {
            if (sentences is not null && sentences.TryGetValue(f.Id, out var sentence) && !string.IsNullOrWhiteSpace(sentence))
            {
                var check = GroundingCheck.Check(sentence, f);
                if (check.Passed)
                {
                    byId[f.Id] = new Explanation(sentence.Trim(), ExplanationSource.Llm);
                    continue;
                }
                rejected.Add(new GroundingRejection(f.Id, check.Problems));
            }
            byId[f.Id] = new Explanation(TemplateExplainer.Explain(f), ExplanationSource.Template);
        }

        return new ExplanationBatch(byId, sentences is null ? DegradedReason.ExplanationFallback : null, result.Call)
        {
            Rejected = rejected,
        };
    }

    /// <summary>Template sentences only (no LLM): used for a late second-opinion item and when the deadline passed.</summary>
    public ExplanationBatch Templates(IReadOnlyList<RankedWork> works, CleanedQuery query, QueryHypothesis? hypothesis)
    {
        var byId = ExplanationFactsBuilder.Build(works, query, hypothesis)
            .ToDictionary(f => f.Id, f => new Explanation(TemplateExplainer.Explain(f), ExplanationSource.Template), StringComparer.Ordinal);
        return new ExplanationBatch(byId, null, null);
    }
}

public sealed record Explanation(string Text, ExplanationSource Source);

/// <param name="Degraded">ExplanationFallback when the LLM call failed or was unavailable (grounding rejections are by design, not degraded).</param>
public sealed record ExplanationBatch(IReadOnlyDictionary<string, Explanation> ByWorkId, DegradedReason? Degraded, LlmCall? Call)
{
    /// <summary>LLM sentences the grounding check rejected (the template was shown instead), for the eval's grounding rate.</summary>
    public IReadOnlyList<GroundingRejection> Rejected { get; init; } = [];
}

/// <summary>One rejected LLM sentence: the work and the check's problem codes (the sentence itself is not kept).</summary>
public sealed record GroundingRejection(string WorkId, IReadOnlyList<string> Problems);
