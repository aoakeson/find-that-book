using FindThatBook.Core.Ports;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// Process-wide LLM guard: requests per minute per provider (sliding window; quotas are per provider, e.g. Gemini free
/// tier 15/min), an estimated daily spend cap, and a per-provider cooldown after a 429. Interpretation has priority:
/// "explain" may take a permit only while more than <see cref="AiBudgetOptions.ExplainReserve"/> are left, so the last
/// few permits of each minute stay with interpretation and a busy minute falls back to template explanations first. A refused call costs no latency: the
/// caller falls back at once (AI-56, SY-08). Every refusal is <see cref="LlmCallStatus.BudgetExhausted"/> (nothing was
/// sent); only a provider's own 429 is RateLimited. Only real provider calls count; cache hits are free.
/// </summary>
public sealed class AiBudget(IOptions<AiOptions> options, TimeProvider time)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.Ordinal);
    private readonly DailyCostMeter _spend = new(time);

    private AiBudgetOptions Options => options.Value.Budget;

    /// <summary>Reserves one provider call for <paramref name="operation"/> ("interpret" | "explain"), or says why not.</summary>
    public BudgetDecision TryAcquire(string provider, string operation)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_cooldownUntil.TryGetValue(provider, out var until) && now < until)
                return BudgetDecision.Deny("cooldown");
            var cap = Options.DailyCostCapUsd;
            if (cap > 0 && _spend.Today >= cap)
                return BudgetDecision.Deny("daily_cap");
            var rpm = options.Value.For(provider)?.RequestsPerMinute ?? 0;
            if (rpm > 0)
            {
                if (!_windows.TryGetValue(provider, out var window)) _windows[provider] = window = new Queue<DateTimeOffset>();
                while (window.Count > 0 && now - window.Peek() >= TimeSpan.FromMinutes(1)) window.Dequeue();
                var left = rpm - window.Count;
                if (left <= 0) return BudgetDecision.Deny("rpm");
                if (IsExplain(operation) && left <= Options.ExplainReserve)
                    return BudgetDecision.Deny("explain_reserve");
                window.Enqueue(now);
            }
            return BudgetDecision.Allow;
        }
    }

    /// <summary>A provider said 429: skip it for Retry-After (bounded) or the configured cooldown.</summary>
    public TimeSpan StartCooldown(string provider, TimeSpan? retryAfter)
    {
        var o = Options;
        var seconds = retryAfter is { } ra && ra > TimeSpan.Zero ? ra.TotalSeconds : o.CooldownSeconds;
        var length = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, o.MaxCooldownSeconds));
        var until = time.GetUtcNow() + length;
        lock (_gate)
        {
            // Concurrent 429s: a short Retry-After must not cut a longer cooldown already running.
            if (!_cooldownUntil.TryGetValue(provider, out var current) || current < until) _cooldownUntil[provider] = until;
        }
        return length;
    }

    private static bool IsExplain(string operation) => string.Equals(operation, "explain", StringComparison.OrdinalIgnoreCase);

    public void RecordSpend(decimal? usd)
    {
        lock (_gate) _spend.Add(usd);
    }

    public decimal SpentToday
    {
        get
        {
            lock (_gate) return _spend.Today;
        }
    }
}

/// <param name="Detail">"cooldown" | "rpm" | "daily_cap" | "explain_reserve" when refused.</param>
public readonly record struct BudgetDecision(bool Allowed, LlmCallStatus Status, string? Detail)
{
    public static BudgetDecision Allow { get; } = new(true, LlmCallStatus.Ok, null);
    public static BudgetDecision Deny(string detail) => new(false, LlmCallStatus.BudgetExhausted, detail);
}

/// <summary>Estimated spend for the current UTC day (resets at midnight UTC). Not thread-safe: callers lock.</summary>
public sealed class DailyCostMeter(TimeProvider time)
{
    private DateOnly _day;
    private decimal _total;

    public decimal Today
    {
        get
        {
            Roll();
            return _total;
        }
    }

    public void Add(decimal? usd)
    {
        Roll();
        if (usd is > 0) _total += usd.Value;
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (today == _day) return;
        _day = today;
        _total = 0;
    }
}

/// <summary>
/// Provider calls that can never succeed (401/403 bad key, 404 unknown model) switch that provider+model+key off
/// until restart or until the admin changes the key/model (AI-55); the next calls skip it as not configured.
/// </summary>
public sealed class ProviderHealth
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _disabled = new(StringComparer.Ordinal);

    public void Disable(ProviderTarget target, string reason)
    {
        lock (_gate) _disabled[target.Identity] = reason;
    }

    public string? DisabledReason(ProviderTarget target)
    {
        lock (_gate) return _disabled.GetValueOrDefault(target.Identity);
    }
}
