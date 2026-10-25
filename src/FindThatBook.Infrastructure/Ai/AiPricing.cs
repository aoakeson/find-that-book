using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Paid-tier cost estimate from AI:Pricing, keyed by the configured model id (exact, case-insensitive).
/// Unknown model → null, never a fake 0.
/// </summary>
public sealed class AiPricing(IOptions<AiOptions> options) : IAiPricing
{
    public decimal? Estimate(string model, long? inputTokens, long? outputTokens)
    {
        if (string.IsNullOrWhiteSpace(model) || inputTokens is null && outputTokens is null) return null;
        var id = model.Trim();
        // The binder may replace the default (case-insensitive) dictionary, so compare explicitly.
        var price = options.Value.Pricing.FirstOrDefault(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
        return price is null ? null : Cost(price.InputPerMTok, price.OutputPerMTok, inputTokens, outputTokens);
    }

    /// <summary>
    /// For daily spend caps only: the estimate, or for an unpriced model the dearest input and output prices in the
    /// table, so a model missing from the table can't switch the cap off (SY-08). Responses still report null.
    /// </summary>
    public decimal? BudgetEstimate(string model, long? inputTokens, long? outputTokens)
    {
        if (Estimate(model, inputTokens, outputTokens) is { } known) return known;
        if (inputTokens is null && outputTokens is null || options.Value.Pricing.Count == 0) return null;
        var prices = options.Value.Pricing.Values;
        return Cost(prices.Max(p => p.InputPerMTok), prices.Max(p => p.OutputPerMTok), inputTokens, outputTokens);
    }

    private static decimal Cost(decimal inputPerMTok, decimal outputPerMTok, long? inputTokens, long? outputTokens) =>
        (Math.Max(0, inputTokens ?? 0) * inputPerMTok + Math.Max(0, outputTokens ?? 0) * outputPerMTok) / 1_000_000m;
}
