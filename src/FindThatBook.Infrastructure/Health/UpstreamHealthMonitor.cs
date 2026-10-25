namespace FindThatBook.Infrastructure.Health;

/// <summary>
/// Tracks upstream service health for the /api/health endpoint without leaking sensitive details.
/// This is a singleton that receives updates from the resilience pipeline and LLM adapters.
/// </summary>
public interface IUpstreamHealthMonitor
{
    UpstreamHealthStatus GetStatus();
}

/// <summary>
/// Health status for upstream services exposed at /api/health.
/// No secrets, internal errors, or detailed diagnostics are included.
/// </summary>
public sealed record UpstreamHealthStatus
{
    /// <summary>Open Library circuit breaker state: "closed" (normal), "open" (blocking calls), "half_open" (testing), or null if unknown.</summary>
    public required string? OpenLibraryCircuit { get; init; }

    /// <summary>UTC timestamp of the last Open Library failure, or null if none recorded.</summary>
    public required DateTimeOffset? OpenLibraryLastFailure { get; init; }

    /// <summary>High-level reason for the last Open Library failure (e.g., "timeout", "unavailable", "rate_limited"), or null.</summary>
    public required string? OpenLibraryLastFailureReason { get; init; }

    /// <summary>LLM provider state: "ok", "rate_limited", "cooldown", "disabled", or null if not configured.</summary>
    public required string? LlmStatus { get; init; }

    /// <summary>UTC timestamp of the last LLM failure, or null if none recorded.</summary>
    public required DateTimeOffset? LlmLastFailure { get; init; }

    /// <summary>High-level reason for the last LLM failure (e.g., "rate_limited", "auth_error", "timeout"), or null.</summary>
    public required string? LlmLastFailureReason { get; init; }
}

/// <summary>
/// Mutable health monitor that components update when failures occur.
/// Thread-safe for concurrent reads and writes.
/// </summary>
public sealed class UpstreamHealthMonitor : IUpstreamHealthMonitor
{
    private readonly object _lock = new();
    private string? _olCircuit = "closed";
    private DateTimeOffset? _olLastFailure;
    private string? _olLastFailureReason;
    private string? _llmStatus;
    private DateTimeOffset? _llmLastFailure;
    private string? _llmLastFailureReason;

    public UpstreamHealthStatus GetStatus()
    {
        lock (_lock)
        {
            return new UpstreamHealthStatus
            {
                OpenLibraryCircuit = _olCircuit,
                OpenLibraryLastFailure = _olLastFailure,
                OpenLibraryLastFailureReason = _olLastFailureReason,
                LlmStatus = _llmStatus,
                LlmLastFailure = _llmLastFailure,
                LlmLastFailureReason = _llmLastFailureReason,
            };
        }
    }

    public void RecordOpenLibraryCircuitState(string state)
    {
        lock (_lock)
        {
            _olCircuit = state;
        }
    }

    public void RecordOpenLibraryFailure(string reason, DateTimeOffset when)
    {
        lock (_lock)
        {
            _olLastFailure = when;
            _olLastFailureReason = reason;
        }
    }

    public void RecordLlmStatus(string status)
    {
        lock (_lock)
        {
            _llmStatus = status;
        }
    }

    public void RecordLlmFailure(string reason, DateTimeOffset when)
    {
        lock (_lock)
        {
            _llmLastFailure = when;
            _llmLastFailureReason = reason;
            _llmStatus = reason == "rate_limited" ? "cooldown" : "error";
        }
    }

    public void ClearLlmFailure()
    {
        lock (_lock)
        {
            _llmStatus = "ok";
        }
    }
}
