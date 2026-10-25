namespace FindThatBook.Core.Model;

/// <summary>
/// A request for books about a subject rather than one particular book. The interpreter decides when it answered
/// (AI first); the cleaner's frame rule ("a book about bears") decides when it did not. One rule, used by the ladder,
/// the ranker and the API, so they cannot disagree.
/// </summary>
/// <param name="Terms">Subject phrases, most specific first (each phrase's words belong together).</param>
/// <param name="FromInterpreter">True when the terms are the interpreter's subjects (most specific first, broader after);
/// false when they are the user's own typed phrases (all equally wanted).</param>
/// <param name="Preferences">Audience/form subject words to prefer when ordering ("juvenile", "fiction").</param>
public sealed record TopicRequest(
    IReadOnlyList<string> Terms,
    string? AuthorHint,
    IReadOnlyList<BookSuggestion> Suggestions,
    bool FromInterpreter = false,
    IReadOnlyList<string>? Preferences = null)
{
    public IReadOnlyList<string> PreferredSubjects => Preferences ?? [];

    public static TopicRequest? From(CleanedQuery query, QueryHypothesis? hypothesis)
    {
        if (hypothesis is not null)
        {
            if (hypothesis.Intent != QueryIntent.Topic) return null;
            var useSubjects = hypothesis.Subjects.Count > 0;
            return new TopicRequest(useSubjects ? hypothesis.Subjects : query.Topic, hypothesis.Author ?? query.TopicAuthor,
                hypothesis.Suggestions, FromInterpreter: useSubjects, query.TopicPreferences);
        }
        return query.Topic.Count > 0
            ? new TopicRequest(query.Topic, query.TopicAuthor, [], FromInterpreter: false, query.TopicPreferences)
            : null;
    }
}
