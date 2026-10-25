using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FindThatBook.Core.Ports;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>search.json URLs and cache keys. Deterministic: the same query always gives the same URL.</summary>
internal static class OpenLibraryUrls
{
    /// <summary>What a result card and the ranker need. Never fields=* (126 KB per doc, DA-071).</summary>
    public const string BaseFields =
        "key,title,subtitle,author_name,author_key,first_publish_year,edition_count,cover_i,contributor,readinglog_count";

    /// <summary>
    /// v3 evidence: pen/real/other-script author names and the best-matching edition's title (DA-007, DA-035), plus that
    /// edition's key so the result link opens on it (the work page otherwise features an edition Open Library picks).
    /// </summary>
    public const string V3Fields = "author_alternative_name,editions,editions.title,editions.key";

    /// <summary>Open Library has no page cap (limit=1000 → 703 docs in 6.6 s, DA-070), so we set one.</summary>
    public const int MaxLimit = 100;

    /// <summary>Rows asked for by the slow-empty re-query.</summary>
    public const int SlowEmptyLimit = 10;

    /// <summary>
    /// The relative search.json URL, or null when the query has nothing to search for. Values are sent exactly as
    /// given (CatalogQueries owns cleaning, Solr escaping and NFC), percent-encoded as UTF-8; person:(…)/OR syntax
    /// in <see cref="CatalogQuery.Q"/> is passed through as text, as are subject:(…) and title:(…) OR title:(…).
    /// <paramref name="slowEmptyRequery"/> drops the edition and alias sub-fields and caps the rows: the lighter
    /// request that answered `bible` in 4.8 s (DA-064). The person and subject fields stay: they are the evidence
    /// those calls exist for.
    /// </summary>
    public static string? Search(CatalogQuery query, bool slowEmptyRequery = false)
    {
        var parts = new List<string>(7);
        Add(parts, "q", query.Q);
        Add(parts, "title", query.Title);
        Add(parts, "author", query.Author);
        Add(parts, "author_key", query.AuthorKey);
        if (parts.Count == 0) return null;

        if (query.Sort == CatalogSort.Editions) parts.Add("sort=editions");
        var fields = slowEmptyRequery ? BaseFields : BaseFields + "," + V3Fields;
        if (query.IncludePersons) fields += ",person";
        if (query.IncludeSubjects) fields += ",subject";
        parts.Add("fields=" + fields); // commas are legal in a query string; kept readable for logs
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        if (slowEmptyRequery) limit = Math.Min(limit, SlowEmptyLimit);
        parts.Add("limit=" + limit.ToString(CultureInfo.InvariantCulture));
        return "search.json?" + string.Join('&', parts);
    }

    /// <summary>
    /// "ol:v1:" + SHA-256 of the absolute URL: always 70 characters. A raw URL key reaches 1,202 characters for a
    /// 300-character CJK query, and HybridCache silently refuses keys over 1,024 (SY-23).
    /// </summary>
    public static string CacheKey(Uri absoluteUrl) =>
        "ol:v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(absoluteUrl.AbsoluteUri)));

    /// <summary>BaseUrl with a trailing slash, so "search.json" resolves under its path rather than replacing it.</summary>
    public static Uri BaseAddress(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parts.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
    }
}

/// <summary>Public Open Library links for result cards (built from PublicUrl/CoversUrl, never the mirror's BaseUrl).</summary>
public static class OpenLibraryLinks
{
    /// <summary>
    /// Work page; an orphan edition id ("OL17318741M", DA-059) links to /books/. With the edition the search matched, the
    /// work page features that edition (<c>?edition=key:/books/OL…M</c>), so "the book of mormon" opens on the English
    /// edition instead of the Spanish one Open Library shows by default.
    /// </summary>
    public static string Work(OpenLibraryOptions options, string workId, string? editionKey = null)
    {
        var isEdition = workId.EndsWith('M');
        var url = OpenLibraryUrls.BaseAddress(options.PublicUrl) + (isEdition ? "books/" : "works/") + Uri.EscapeDataString(workId);
        return !isEdition && editionKey is { Length: > 2 } && editionKey.StartsWith("OL", StringComparison.Ordinal) && editionKey.EndsWith('M')
               && editionKey[2..^1].All(char.IsAsciiDigit)
            ? url + "?edition=key%3A/books/" + editionKey
            : url;
    }

    /// <summary>Cover image by cover id and size S, M or L; null when there is no cover.</summary>
    public static string? Cover(OpenLibraryOptions options, int? coverId, char size = 'M') =>
        coverId is > 0 && size is 'S' or 'M' or 'L'
            ? $"{OpenLibraryUrls.BaseAddress(options.CoversUrl)}b/id/{coverId.Value.ToString(CultureInfo.InvariantCulture)}-{size}.jpg"
            : null;
}
