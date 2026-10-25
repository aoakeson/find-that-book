using FindThatBook.Core.Model;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>One search.json page, mapped. This is what the cache stores.</summary>
/// <param name="SlowEmptyRetried">Hits that came from the slow-empty re-query (IN-064); such pages are never cached.</param>
internal sealed record SearchPage(IReadOnlyList<CandidateWork> Works, int NumFound, bool SlowEmptyRetried = false);

/// <summary>Vendor DTO → <see cref="CandidateWork"/>. Every quirk of the data is absorbed here.</summary>
internal static class OpenLibraryMapper
{
    public const int MaxEditionTitles = 3;
    public const int MaxAlternativeNames = 60;
    public const int MaxSubjects = 40;

    /// <param name="currentYear">UTC year now; a first_publish_year after next year is a data error (DA-061).</param>
    public static SearchPage ToPage(OlSearchResponse response, int currentYear)
    {
        var works = new List<CandidateWork>(response.Docs?.Count ?? 0);
        foreach (var doc in response.Docs ?? [])
        {
            if (doc is not null && ToWork(doc, currentYear) is { } work) works.Add(work);
        }
        return new SearchPage(works, Math.Max(response.NumFound ?? works.Count, 0));
    }

    /// <summary>Null for a doc we can't identify or show (no key or no title).</summary>
    public static CandidateWork? ToWork(OlSearchDoc doc, int currentYear)
    {
        var id = BareKey(doc.Key);
        if (id is null || string.IsNullOrWhiteSpace(doc.Title)) return null;
        return new CandidateWork
        {
            WorkId = id,
            Title = doc.Title,
            Subtitle = string.IsNullOrWhiteSpace(doc.Subtitle) ? null : doc.Subtitle,
            AuthorNames = NonBlank(doc.AuthorName),
            AuthorKeys = [.. NonBlank(doc.AuthorKey).Select(BareKey).OfType<string>()],
            Contributors = NonBlank(doc.Contributor),
            FirstPublishYear = PlausibleYear(doc.FirstPublishYear, currentYear),
            EditionCount = Math.Max(doc.EditionCount ?? 0, 0),
            CoverId = doc.CoverId > 0 ? doc.CoverId : null,                              // -1 means "no cover"
            ReadingLogCount = Math.Max(doc.ReadingLogCount ?? 0, 0),
            EditionKey = doc.Editions?.Docs?.Select(e => BareKey(e?.Key)).FirstOrDefault(k => k is not null && k.EndsWith('M')),
            EditionTitles = Distinct(doc.Editions?.Docs?.Select(e => e?.Title), MaxEditionTitles),
            AuthorAlternativeNames = Distinct(doc.AuthorAlternativeName, MaxAlternativeNames),
            Persons = Distinct(doc.Person, int.MaxValue),
            Subjects = [.. NonBlank(doc.Subject).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSubjects)],
        };
    }

    /// <summary>DA-061: 0 (War of the Worlds) or a year after next year is no year at all.</summary>
    public static int? PlausibleYear(int? year, int currentYear) => year is { } y && y > 0 && y <= currentYear + 1 ? y : null;

    /// <summary>"/works/OL27482W" → "OL27482W"; a bare key passes through; blank → null.</summary>
    public static string? BareKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var bare = key.Trim().TrimEnd('/');
        var slash = bare.LastIndexOf('/');
        bare = slash < 0 ? bare : bare[(slash + 1)..];
        return bare.Length == 0 ? null : bare;
    }

    private static string[] NonBlank(IEnumerable<string?>? values) =>
        values is null ? [] : [.. values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!)];

    private static string[] Distinct(IEnumerable<string?>? values, int max) =>
        [.. NonBlank(values).Distinct(StringComparer.Ordinal).Take(max)];
}
