using System.Text.Json;
using System.Text.Json.Serialization;

namespace FindThatBook.Infrastructure.OpenLibrary;

// Vendor shapes of search.json (anti-corruption layer): everything optional, nothing leaks past OpenLibraryMapper.

internal sealed class OlSearchResponse
{
    [JsonPropertyName("numFound")] public int? NumFound { get; init; }
    [JsonPropertyName("docs")] public List<OlSearchDoc?>? Docs { get; init; }

    public static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

internal sealed class OlSearchDoc
{
    /// <summary>"/works/OL27482W"; orphan editions come back as "/works/OL17318741M" (DA-059).</summary>
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }
    [JsonPropertyName("author_name")] public List<string?>? AuthorName { get; init; }

    /// <summary>Already bare in search results ("OL26320A"); the mapper accepts both forms.</summary>
    [JsonPropertyName("author_key")] public List<string?>? AuthorKey { get; init; }
    [JsonPropertyName("first_publish_year")] public int? FirstPublishYear { get; init; }
    [JsonPropertyName("edition_count")] public int? EditionCount { get; init; }
    [JsonPropertyName("cover_i")] public int? CoverId { get; init; }
    [JsonPropertyName("contributor")] public List<string?>? Contributor { get; init; }
    [JsonPropertyName("readinglog_count")] public int? ReadingLogCount { get; init; }
    [JsonPropertyName("author_alternative_name")] public List<string?>? AuthorAlternativeName { get; init; }

    /// <summary>The best-matching edition(s) for this query: {"numFound":1,"docs":[{"title":"Death's End"}]}.</summary>
    [JsonPropertyName("editions")] public OlEditions? Editions { get; init; }
    [JsonPropertyName("person")] public List<string?>? Person { get; init; }

    /// <summary>Subject headings, often 20-100 per work ("Bears", "Teddy bears", "Juvenile fiction").</summary>
    [JsonPropertyName("subject")] public List<string?>? Subject { get; init; }
}

internal sealed class OlEditions
{
    [JsonPropertyName("docs")] public List<OlEdition?>? Docs { get; init; }
}

internal sealed class OlEdition
{
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
}
