namespace FindThatBook.Core.Model;

/// <summary>
/// The interpreter's guess. A hypothesis only steers retrieval and ranking: a book is returned only if Open Library
/// has it. For a topic request ("find me a book about bears") it carries subjects and suggested titles instead of
/// one title.
/// </summary>
public sealed record QueryHypothesis(
    string? Title,
    string? Author,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Keywords,
    string? EnglishTitle,
    string? OriginalTitle)
{
    /// <summary>Whether the text points at one particular book or asks for books about a subject.</summary>
    public QueryIntent Intent { get; init; } = QueryIntent.Book;

    /// <summary>Topic requests: 1–3 short English subject headings ("bears", "french revolution").</summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];

    /// <summary>Topic requests: well-known books the interpreter thinks fit; each must still be found in Open Library.</summary>
    public IReadOnlyList<BookSuggestion> Suggestions { get; init; } = [];

    public bool IsEmpty => Title is null && Author is null && Characters.Count == 0 && EnglishTitle is null && OriginalTitle is null
                           && Subjects.Count == 0 && Suggestions.Count == 0;
}

public sealed record BookSuggestion(string Title, string? Author);
