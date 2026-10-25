namespace FindThatBook.Infrastructure.Suggest;

/// <summary>
/// Autocomplete over a prebuilt local index of real works: no API calls on the keystroke path.
/// </summary>
public interface ISuggestIndex
{
    /// <summary>Index version and size, for /api/health and docs.</summary>
    string Version { get; }
    int Count { get; }

    /// <summary>Whole-title prefix &gt; title-word prefix &gt; author-surname prefix, then edition count. Empty prefix → empty list.</summary>
    IReadOnlyList<Suggestion> Lookup(string prefix, int limit);
}

public enum SuggestMatch { Title, Word, Author }

public sealed record Suggestion(string WorkId, string Title, string? Author, int? FirstPublishYear, int? CoverId, int EditionCount, SuggestMatch Match);
