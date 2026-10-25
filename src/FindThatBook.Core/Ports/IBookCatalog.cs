using FindThatBook.Core.Model;

namespace FindThatBook.Core.Ports;

/// <summary>
/// The catalog (Open Library search.json). One method: search documents already carry author keys in works.authors order,
/// contributor roles, edition titles and alternative names, so the hot path needs no /works or /authors calls.
/// </summary>
public interface IBookCatalog
{
    /// <summary>
    /// Runs one logical search. Implementations own HTTP retries, throttling and caching, and report them in
    /// <see cref="CatalogResponse.Stats"/>. Throws <see cref="CatalogException"/> on failure; caller cancellation
    /// surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct);
}

public enum CatalogSort { Relevance, Editions }

/// <summary>
/// search.json parameters. <see cref="Q"/> may contain fielded syntax built by <c>CatalogQueries</c>
/// (person:(…), OR-joins); every other value is sent as a plain parameter.
/// </summary>
public sealed record CatalogQuery
{
    public string? Q { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? AuthorKey { get; init; }
    public CatalogSort Sort { get; init; } = CatalogSort.Relevance;
    public int Limit { get; init; } = 20;

    /// <summary>Also request the person field (character names).</summary>
    public bool IncludePersons { get; init; }

    /// <summary>Also request the subject field (topic requests).</summary>
    public bool IncludeSubjects { get; init; }

    public override string ToString() => string.Join('&', new[]
    {
        Q is null ? null : $"q={Q}", Title is null ? null : $"title={Title}", Author is null ? null : $"author={Author}",
        AuthorKey is null ? null : $"author_key={AuthorKey}", Sort == CatalogSort.Editions ? "sort=editions" : null,
        $"limit={Limit}", IncludePersons ? "person" : null, IncludeSubjects ? "subject" : null,
    }.Where(p => p is not null));
}

/// <summary>A pipeline step's request: <see cref="Step"/> names it in meta.open_library.steps.</summary>
public sealed record CatalogRequest(string Step, CatalogQuery Query);

public sealed record CatalogResponse(IReadOnlyList<CandidateWork> Works, int NumFound, CatalogCallStats Stats);

/// <summary>What one logical call cost upstream.</summary>
/// <param name="CacheHit">Served from the cache: no HTTP request was made.</param>
/// <param name="Attempts">HTTP requests actually sent (1 + retries; 0 on a cache hit).</param>
/// <param name="ElapsedMs">Wall time of the logical call.</param>
/// <param name="SlowEmptyRetried">A slow 200 with 0 hits was treated as a failure and re-queried once (IN-064/DA-064).</param>
public sealed record CatalogCallStats(bool CacheHit, int Attempts, long ElapsedMs, bool SlowEmptyRetried = false);
