using FindThatBook.Infrastructure.Suggest;

namespace FindThatBook.Api.Contracts;

/// <summary>Autocomplete from a prebuilt local index of real works: no Open Library or LLM call per keystroke.</summary>
public sealed record SuggestResponse
{
    /// <summary>The prefix as looked up (trimmed, control characters removed). Empty → no suggestions.</summary>
    public required string Prefix { get; init; }
    public required IReadOnlyList<SuggestionItem> Suggestions { get; init; }
}

public sealed record SuggestionItem
{
    public required string WorkId { get; init; }
    public required string Title { get; init; }

    /// <summary>Primary author, when the index has one.</summary>
    public required string? Author { get; init; }
    public required int? FirstPublishYear { get; init; }
    public required string? CoverUrl { get; init; }
    public required int EditionCount { get; init; }
    public required string OpenLibraryUrl { get; init; }

    /// <summary>title: the whole title starts with the prefix · word: a title word does · author: the author's surname does.</summary>
    public required SuggestMatch Match { get; init; }
}

/// <summary>Liveness only: never calls Open Library or an LLM, so probes can't burn quota.</summary>
public sealed record HealthResponse
{
    /// <summary>Always "ok" when the process answers.</summary>
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required string Environment { get; init; }

    /// <summary>The active LLM provider (gemini, anthropic, openai, none); null if unknown.</summary>
    public required string? AiProvider { get; init; }

    /// <summary>Whether the active provider has a key. False means heuristic parsing and template explanations.</summary>
    public required bool? AiConfigured { get; init; }
    public required bool? JudgeEnabled { get; init; }
    public required SuggestIndexInfo? SuggestIndex { get; init; }

    /// <summary>Upstream service health status (Open Library circuit, LLM status) without sensitive details.</summary>
    public required UpstreamHealth? Upstream { get; init; }
}

/// <summary>Upstream service health indicators exposed at /api/health (SY-34).</summary>
public sealed record UpstreamHealth
{
    /// <summary>Open Library circuit breaker state: "closed" (normal), "open" (blocking calls), "half_open" (testing).</summary>
    public required string? OpenLibraryCircuit { get; init; }

    /// <summary>ISO 8601 timestamp of the last Open Library failure, or null if none recorded.</summary>
    public required DateTimeOffset? OpenLibraryLastFailure { get; init; }

    /// <summary>High-level reason for the last failure (e.g., "timeout", "unavailable"), or null.</summary>
    public required string? OpenLibraryLastFailureReason { get; init; }

    /// <summary>LLM provider state: "ok", "rate_limited", "cooldown", "disabled", or null if not configured.</summary>
    public required string? LlmStatus { get; init; }

    /// <summary>ISO 8601 timestamp of the last LLM failure, or null if none recorded.</summary>
    public required DateTimeOffset? LlmLastFailure { get; init; }

    /// <summary>High-level reason for the last LLM failure (e.g., "rate_limited", "timeout"), or null.</summary>
    public required string? LlmLastFailureReason { get; init; }
}

public sealed record SuggestIndexInfo
{
    public required string Version { get; init; }
    public required int Count { get; init; }
}
