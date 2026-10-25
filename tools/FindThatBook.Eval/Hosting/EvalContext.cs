using System.Collections.Concurrent;

namespace FindThatBook.Eval.Hosting;

/// <summary>
/// Per-query side channel for the eval's own instruments (replay catalog, recording handler). The runner sets it around
/// one SearchAsync call; it flows through awaits into the catalog and the HTTP handlers, so concurrent queries never mix.
/// </summary>
public sealed class QueryProbe(string caseId)
{
    public string CaseId { get; } = caseId;
    public ConcurrentQueue<string> ReplayMisses { get; } = new();
    public ConcurrentQueue<string> ReplayHits { get; } = new();

    /// <summary>Hits on a recording that cannot fully answer the request (fewer hits than the limit asked, or no person field).</summary>
    public ConcurrentQueue<string> ReplayPartial { get; } = new();
    public ConcurrentQueue<string> Recorded { get; } = new();

    /// <summary>Recordings fetched from the backend on a cache miss (record-on-miss catalog cache).</summary>
    public ConcurrentQueue<string> CacheFills { get; } = new();

    /// <summary>Requests the backend cannot answer faithfully (person: on the mirror), answered empty without a call.</summary>
    public ConcurrentQueue<string> Unsupported { get; } = new();

    /// <summary>Queries the hypothesis replay had no entry for (the row ran LLM-off).</summary>
    public ConcurrentQueue<string> HypothesisMisses { get; } = new();
}

public static class EvalContext
{
    private static readonly AsyncLocal<QueryProbe?> Probe = new();

    public static QueryProbe? Current => Probe.Value;

    public static IDisposable Begin(QueryProbe probe)
    {
        var previous = Probe.Value;
        Probe.Value = probe;
        return new Restore(previous);
    }

    private sealed class Restore(QueryProbe? previous) : IDisposable
    {
        public void Dispose() => Probe.Value = previous;
    }
}
