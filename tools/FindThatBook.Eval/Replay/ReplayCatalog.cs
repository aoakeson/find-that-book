using System.Collections.Concurrent;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Replay;

/// <summary>
/// Offline <see cref="IBookCatalog"/>: answers each logical search from a recorded response with the same parameters
/// (<see cref="RequestKey"/>). A miss returns an empty result AND is reported on the query's probe as replay_miss; a
/// recording that cannot fully answer (shorter limit, no person field) is used but reported as replay_partial. The
/// summary lists both, so neither is silently scored as what Open Library would have returned.
/// No HTTP happens, so stats report 0 attempts and 0 ms (replay latency is not Open Library latency).
/// </summary>
/// <remarks>
/// With a <c>fallback</c> it is the record-on-miss catalog cache: a miss goes to the real adapter (the mirror or live
/// Open Library), whose response the recording handler saves to disk; the new recording joins the index and the answer
/// is mapped from it, so a fetched and a replayed response are mapped by the same code in every arm.
/// <c>unsupported</c> marks requests the backend cannot answer faithfully (person: on the mirror): they get an empty
/// result without a call, counted per row.
/// </remarks>
public sealed class ReplayCatalog(RecordedResponseIndex index, Func<IBookCatalog>? fallback = null, Func<CatalogQuery, bool>? unsupported = null,
    RecordingSink? sink = null)
    : IBookCatalog
{
    private int _fills;
    private int _unsupported;

    public bool RecordsOnMiss => fallback is not null;
    public int Fills => _fills;
    public int UnsupportedCalls => _unsupported;

    private readonly ConcurrentDictionary<string, Usage> _used = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _misses = new(StringComparer.Ordinal);

    public RecordedResponseIndex Index { get; } = index;

    /// <summary>Recordings that answered at least one request, with the largest limit asked of each and who asked.</summary>
    public IReadOnlyCollection<Usage> Used => [.. _used.Values];

    /// <summary>Requests with no recording → the case ids that made them.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Misses =>
        _misses.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)[.. kv.Value.Distinct().Order(StringComparer.Ordinal)], StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fillLocks = new(StringComparer.Ordinal);
    private readonly Lock _drainGate = new();

    public async Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = RequestKey.From(query);
        var probe = EvalContext.Current;
        var hit = Index.Find(key, query.Limit, query.IncludePersons);
        if (fallback is not null && !Complete(hit, query))
        {
            if (unsupported?.Invoke(query) == true)
            {
                Interlocked.Increment(ref _unsupported);
                probe?.Unsupported.Enqueue(key.Text);
                return new CatalogResponse([], 0, new CatalogCallStats(CacheHit: false, Attempts: 0, ElapsedMs: 0));
            }
            // One fetch per key at a time: a second query missing the same key waits and is then answered from the
            // recording, so every arm and every query sees one response per request.
            var gate = _fillLocks.GetOrAdd(key.IndexKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                hit = Index.Find(key, query.Limit, query.IncludePersons);
                if (Complete(hit, query)) return Answer(hit!, query, probe, key, new CatalogCallStats(CacheHit: false, Attempts: 0, ElapsedMs: 0));
                var fetched = await fallback().SearchAsync(query, ct);
                Interlocked.Increment(ref _fills);
                probe?.CacheFills.Enqueue(key.Text);
                lock (_drainGate) // dequeue + index atomically, so no caller can miss a file another caller took
                {
                    while (sink?.Saved.TryDequeue(out var file) == true)
                        if (RecordedResponseIndex.TryRead(file, 0) is { } recorded) Index.Add(recorded);
                }
                hit = Index.Find(key, query.Limit, query.IncludePersons);
                if (hit is null) return fetched; // e.g. a non-200 the handler did not record: the adapter's own answer
                return Answer(hit, query, probe, key, fetched.Stats);
            }
            finally
            {
                gate.Release();
            }
        }
        if (hit is null)
        {
            probe?.ReplayMisses.Enqueue(key.Text);
            _misses.GetOrAdd(key.Text, _ => []).Add(probe?.CaseId ?? "?");
            return new CatalogResponse([], 0, new CatalogCallStats(CacheHit: false, Attempts: 0, ElapsedMs: 0));
        }
        return Answer(hit, query, probe, key, new CatalogCallStats(CacheHit: false, Attempts: 0, ElapsedMs: 0));
    }

    /// <summary>A recording that fully answers the request: covers the limit, and carries person when it was asked for.</summary>
    private static bool Complete(RecordedResponse? hit, CatalogQuery query) =>
        hit is not null && hit.Covers(query.Limit) && (!query.IncludePersons || hit.Has("person"));

    private CatalogResponse Answer(RecordedResponse hit, CatalogQuery query, QueryProbe? probe, RequestKey key, CatalogCallStats stats)
    {
        probe?.ReplayHits.Enqueue(key.Text);
        // The best recording may still be short (recorded with a smaller limit) or lack person: say so, never hide it.
        if (!hit.Covers(query.Limit)) probe?.ReplayPartial.Enqueue($"{key.Text} (recorded limit {hit.Request.Limit}, asked {query.Limit})");
        else if (query.IncludePersons && !hit.Has("person")) probe?.ReplayPartial.Enqueue($"{key.Text} (recording has no person field)");
        var usage = _used.GetOrAdd(hit.File, _ => new Usage(hit));
        usage.Note(query.Limit, probe?.CaseId);
        var works = hit.Docs.Take(query.Limit)
            .Select(d => SearchDocMapper.Map(d, query.IncludePersons))
            .OfType<CandidateWork>()
            .ToList();
        return new CatalogResponse(works, hit.NumFound ?? works.Count, stats);
    }

    public sealed class Usage(RecordedResponse response)
    {
        private readonly Lock _gate = new();
        private readonly SortedSet<string> _cases = new(StringComparer.Ordinal);

        public RecordedResponse Response { get; } = response;
        public int MaxLimit { get; private set; }

        public IReadOnlyList<string> Cases
        {
            get { lock (_gate) return [.. _cases]; }
        }

        public void Note(int limit, string? caseId)
        {
            lock (_gate)
            {
                MaxLimit = Math.Max(MaxLimit, limit);
                if (caseId is not null) _cases.Add(caseId);
            }
        }
    }
}
