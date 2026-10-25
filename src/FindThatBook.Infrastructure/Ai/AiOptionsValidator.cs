using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Fails startup on a bad AI section (SY-36) instead of failing the first search.</summary>
internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions o)
    {
        var errors = new List<string>();
        if (AiProviders.Normalize(o.Provider) is null)
            errors.Add($"AI:Provider must be auto, gemini, anthropic, openai or none (got '{o.Provider}').");

        foreach (var provider in AiProviders.All)
        {
            var p = o.For(provider)!;
            if (!ModelIds.IsValid(p.Model)) errors.Add($"AI:{provider}:Model is missing or invalid.");
            foreach (var (op, n) in p.MaxOutputTokens)
                if (n is < 16 or > 16000) errors.Add($"AI:{provider}:MaxOutputTokens:{op} must be 16..16000.");
            if (p.Temperature is { } t && (t < 0 || t > 2)) errors.Add($"AI:{provider}:Temperature must be 0..2.");
            if (p.RequestsPerMinute < 0) errors.Add($"AI:{provider}:RequestsPerMinute must be >= 0.");
        }

        foreach (var level in o.Gemini.ThinkingLevelByModel.Values.Append(o.Gemini.ThinkingLevel).OfType<string>())
            if (level.ToLowerInvariant() is not ("minimal" or "low" or "medium" or "high"))
                errors.Add($"AI:Gemini:ThinkingLevel '{level}' must be minimal, low, medium or high.");
        if (o.Gemini.ThinkingBudget is < 0) errors.Add("AI:Gemini:ThinkingBudget must be >= 0.");
        if (o.Anthropic.Thinking.ToLowerInvariant() is not ("disabled" or "adaptive"))
            errors.Add("AI:Anthropic:Thinking must be disabled or adaptive.");
        if (o.Anthropic.Effort is { } effort && effort.ToLowerInvariant() is not ("low" or "medium" or "high"))
            errors.Add("AI:Anthropic:Effort must be low, medium or high (thinking 'disabled' rejects xhigh/max).");
        if (o.OpenAI.ReasoningEffort is { } re && re.ToLowerInvariant() is not ("minimal" or "low" or "medium" or "high" or "none"))
            errors.Add("AI:OpenAI:ReasoningEffort must be none, minimal, low, medium or high.");

        foreach (var (op, opt) in new[] { ("Interpret", o.Interpret), ("Explain", o.Explain) })
        {
            if (opt.TimeoutMs is < 100 or > 30000) errors.Add($"AI:{op}:TimeoutMs must be 100..30000.");
            if (opt.MaxOutputTokens is < 16 or > 16000) errors.Add($"AI:{op}:MaxOutputTokens must be 16..16000.");
        }

        if (o.Cache.TtlMinutes < 1) errors.Add("AI:Cache:TtlMinutes must be >= 1.");
        if (o.Budget.ExplainReserve < 0) errors.Add("AI:Budget:ExplainReserve must be >= 0.");
        if (o.Budget.DailyCostCapUsd < 0) errors.Add("AI:Budget:DailyCostCapUsd must be >= 0.");
        if (o.Budget.CooldownSeconds < 0 || o.Budget.MaxCooldownSeconds < o.Budget.CooldownSeconds)
            errors.Add("AI:Budget:CooldownSeconds must be >= 0 and <= MaxCooldownSeconds.");
        foreach (var (model, price) in o.Pricing)
            if (price.InputPerMTok < 0 || price.OutputPerMTok < 0) errors.Add($"AI:Pricing:{model} must not be negative.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal static class ModelIds
{
    /// <summary>Model ids are short ASCII tokens ("gemini-3.5-flash-lite", "claude-haiku-5-5", "models/x").</summary>
    public static bool IsValid(string? model) =>
        !string.IsNullOrWhiteSpace(model) && model.Length <= 100
        && char.IsAsciiLetterOrDigit(model[0])
        && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '/');
}
