using System.Text.Json;
using FindThatBook.Core.Model;

namespace FindThatBook.Eval.Replay;

/// <summary>
/// search.json doc → <see cref="CandidateWork"/> for the replay catalog, kept rule-for-rule with the adapter's
/// internal OpenLibraryMapper (bare ids, cover ≤ 0 → null, implausible first_publish_year → null (R5, DA-061), ≤ 3
/// distinct edition titles, ≤ 60 distinct alternative names). One replay-only difference: persons are kept only when
/// the request asked for them, because one recording can answer both a person: and a plain request.
/// Known bias until the adapter's mapper is shared: replay does not exercise the adapter's DTO code (it has its own tests).
/// </summary>
public static class SearchDocMapper
{
    public const int MaxEditionTitles = 3;
    public const int MaxAlternativeNames = 60;

    public static CandidateWork? Map(JsonElement doc, bool includePersons)
    {
        if (Str(doc, "key") is not { } key || Str(doc, "title") is not { } title) return null;
        var editionTitles = doc.TryGetProperty("editions", out var eds) && eds.ValueKind == JsonValueKind.Object
                            && eds.TryGetProperty("docs", out var edDocs) && edDocs.ValueKind == JsonValueKind.Array
            ? edDocs.EnumerateArray().Select(e => Str(e, "title")).OfType<string>().Distinct(StringComparer.Ordinal).Take(MaxEditionTitles).ToList()
            : [];
        return new CandidateWork
        {
            WorkId = key[(key.LastIndexOf('/') + 1)..],
            Title = title,
            Subtitle = Str(doc, "subtitle"),
            AuthorNames = Strs(doc, "author_name"),
            AuthorKeys = Strs(doc, "author_key").Select(k => k[(k.LastIndexOf('/') + 1)..]).ToList(),
            Contributors = Strs(doc, "contributor"),
            FirstPublishYear = Int(doc, "first_publish_year") is { } y && y > 0 && y <= DateTime.UtcNow.Year + 1 ? y : null,
            EditionCount = Int(doc, "edition_count") ?? 0,
            CoverId = Int(doc, "cover_i") is > 0 and var c ? c : null,
            ReadingLogCount = Int(doc, "readinglog_count") ?? 0,
            EditionTitles = editionTitles,
            AuthorAlternativeNames = Strs(doc, "author_alternative_name").Distinct(StringComparer.Ordinal).Take(MaxAlternativeNames).ToList(),
            Persons = includePersons ? Strs(doc, "person").Distinct(StringComparer.Ordinal).ToList() : [],
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static IReadOnlyList<string> Strs(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
            : [];
}
