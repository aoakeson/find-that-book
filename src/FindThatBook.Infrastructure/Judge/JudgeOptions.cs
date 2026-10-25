using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Judge;

/// <summary>Configuration section "Judge": OpenAI Decisions as an optional status gate. Off by default.</summary>
public sealed class JudgeOptions
{
    public const string Section = "Judge";

    public bool Enabled { get; set; }
    public string Model { get; set; } = "gpt-6-luna";
    public string BaseUrl { get; set; } = "https://api.openai.com/";

    /// <summary>Optional; falls back to AI:OpenAI:ApiKey.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The single attempt (no retries); the judge must never hold up the response.</summary>
    public int TimeoutMs { get; set; } = 1500;

    /// <summary>After a 429 the judge is skipped for Retry-After (bounded by the max) or this many seconds.</summary>
    public int CooldownSeconds { get; set; } = 30;
    public int MaxCooldownSeconds { get; set; } = 300;

    public int MaxCandidates { get; set; } = 8;

    /// <summary>Request caps (AI-46: one anthology with 150 authors cost 21x the tokens).</summary>
    public int MaxAuthorsPerCandidate { get; set; } = 5;
    public int MaxContributorsPerCandidate { get; set; } = 5;
    public int MaxTitleChars { get; set; } = 200;
    public int MaxDescriptionChars { get; set; } = 200;

    /// <summary>Verdicts repeat exactly for identical input, so they are cached (AI-54).</summary>
    public int CacheMinutes { get; set; } = 1440;

    /// <summary>Estimated spend per UTC day (SY-08). 0 = unlimited.</summary>
    public decimal DailyCostCapUsd { get; set; } = 0.50m;
}

internal sealed class JudgeOptionsValidator : IValidateOptions<JudgeOptions>
{
    public ValidateOptionsResult Validate(string? name, JudgeOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.Model)) errors.Add("Judge:Model is required.");
        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            errors.Add("Judge:BaseUrl must be an absolute http(s) URL.");
        if (o.TimeoutMs is < 100 or > 10000) errors.Add("Judge:TimeoutMs must be 100..10000.");
        if (o.CooldownSeconds < 0 || o.MaxCooldownSeconds < o.CooldownSeconds) errors.Add("Judge:CooldownSeconds must be >= 0 and <= MaxCooldownSeconds.");
        if (o.MaxCandidates is < 2 or > 20) errors.Add("Judge:MaxCandidates must be 2..20.");
        if (o.MaxAuthorsPerCandidate < 1 || o.MaxContributorsPerCandidate < 0) errors.Add("Judge author/contributor caps are invalid.");
        if (o.MaxTitleChars < 20 || o.MaxDescriptionChars < 40) errors.Add("Judge title/description caps are too small.");
        if (o.CacheMinutes < 0) errors.Add("Judge:CacheMinutes must be >= 0.");
        if (o.DailyCostCapUsd < 0) errors.Add("Judge:DailyCostCapUsd must be >= 0.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
