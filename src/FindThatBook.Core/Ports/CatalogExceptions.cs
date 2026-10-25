namespace FindThatBook.Core.Ports;

/// <summary>A catalog call failed after the adapter's own retries. <see cref="Attempts"/> is the HTTP requests sent.</summary>
public abstract class CatalogException(string message, int attempts, Exception? inner = null) : Exception(message, inner)
{
    public int Attempts { get; } = attempts;
}

/// <summary>5xx/429 after retries, an open circuit, or our own throttle queue overflowing. Maps to 503.</summary>
public sealed class CatalogUnavailableException(string message, int attempts, TimeSpan? retryAfter = null, Exception? inner = null)
    : CatalogException(message, attempts, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Every attempt timed out. Maps to 504.</summary>
public sealed class CatalogTimeoutException(string message, int attempts, Exception? inner = null)
    : CatalogException(message, attempts, inner);

/// <summary>Unparseable JSON or an unexpected non-transient status. Maps to 502.</summary>
public sealed class CatalogBadResponseException(string message, int attempts, Exception? inner = null)
    : CatalogException(message, attempts, inner);

/// <summary>Open Library rejected the query itself (422 "Query too short", Solr syntax). Never a 5xx: the pipeline re-plans.</summary>
public sealed class CatalogQueryRejectedException(string message, int attempts, Exception? inner = null)
    : CatalogException(message, attempts, inner);

/// <summary>
/// The caller cancelled mid-call (request deadline or client abort). Still an <see cref="OperationCanceledException"/>,
/// so cancellation handling is unchanged; <see cref="Attempts"/> is the HTTP requests already sent.
/// </summary>
public sealed class CatalogCancelledException(int attempts, Exception? inner, CancellationToken token)
    : OperationCanceledException("The catalog call was cancelled by the caller.", inner, token)
{
    public int Attempts { get; } = attempts;
}
