namespace FindThatBook.Core.Ports;

public enum LlmCallStatus { Ok, NotConfigured, BudgetExhausted, Timeout, RateLimited, ProviderError, MalformedOutput, Truncated, Refused }

/// <summary>One LLM call as measured by the adapter. Token counts are null when the provider did not report them.</summary>
public sealed record LlmCall(
    string Operation,
    string Provider,
    string Model,
    string PromptId,
    LlmCallStatus Status,
    bool CacheHit,
    long? InputTokens,
    long? OutputTokens,
    long ElapsedMs,
    string? Detail = null)
{
    public bool Succeeded => Status == LlmCallStatus.Ok;
}

/// <summary>An adapter result. Adapters never throw (except caller cancellation): failure is a null value + a status.</summary>
public sealed record LlmResult<T>(T? Value, LlmCall Call) where T : class;
