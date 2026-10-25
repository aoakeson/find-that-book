using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Retrieval;

/// <summary>
/// Decides which catalog calls to make, in the measured order: broad → hypothesis / original title → [weak pool: person
/// search → OR search] → character fallback → author top works (→ R3-0195 mashup probe). It only decides;
/// <c>SearchService</c> executes and enforces the call budget and deadline. Each stage runs at most once and a later
/// stage closes the earlier ones, so the stages run as a single pass.
/// </summary>
public static class RetrievalLadder
{
    /// <summary>
    /// Calls justified by the hypothesis alone (its title adds words; an original-language title), or, for a topic request,
    /// the subject search and the suggestion check instead. May run beside call 1.
    /// </summary>
    public static IReadOnlyList<CatalogRequest> AfterInterpretation(CleanedQuery query, QueryHypothesis? hypothesis)
    {
        if (query.IsEmpty) return [];
        var calls = new List<CatalogRequest>();
        var interpreted = NormalizedHypothesis.From(hypothesis);
        var topic = TopicRequest.From(query, hypothesis);
        // A topic request has no one book to look up: the suggestion check verifies the books the interpreter named.
        if (interpreted is not null && topic is null)
        {
            if ((interpreted.HasTitle || interpreted.Surname.Length > 0) && AddsWords(query, interpreted)
                && CatalogQueries.HypothesisTitle(hypothesis!) is { } titleCall)
                calls.Add(new CatalogRequest(CatalogSteps.Hypothesis, titleCall));
            // A non-English canonical record carries the original title; the English title only matches its edition titles.
            if (interpreted.Alternates.Any(a => a.IsOriginal) && CatalogQueries.OriginalTitle(hypothesis!) is { } originalCall)
                calls.Add(new CatalogRequest(CatalogSteps.OriginalTitle, originalCall));
        }
        // Topic request ("find me a book about bears"): a subject search, and one title search verifying the interpreter's
        // suggested books. The broad call is unchanged, so a typed real title still wins.
        if (topic is not null)
        {
            if (TopicCall(topic) is { } topicCall)
                calls.Add(new CatalogRequest(CatalogSteps.Topic, topicCall));
            if (CatalogQueries.Suggestions(topic.Suggestions) is { } suggestionsCall)
                calls.Add(new CatalogRequest(CatalogSteps.Suggestions, suggestionsCall));
        }
        return calls;
    }

    /// <summary>
    /// Focused retry: the hypothesis repeats typed words, but nothing in call 1's pool is that title (by that author), so
    /// the focused title call runs anyway (chatty queries: "…it said The Wednesday Wars and im already late").
    /// </summary>
    public static IReadOnlyList<CatalogRequest> AfterBroad(CleanedQuery query, QueryHypothesis? hypothesis, IReadOnlyList<CandidateWork> pool, IReadOnlyCollection<string> stepsIssued)
    {
        var h = NormalizedHypothesis.From(hypothesis);
        if (h is null || !h.HasTitle || stepsIssued.Contains(CatalogSteps.Hypothesis) || TopicRequest.From(query, hypothesis) is not null) return [];
        var present = pool.Any(w =>
            TokenMatch.Score(h.Main, TitleText.Sets(w.Title).Main).Exact
            && (h.Surname.Length == 0 || w.AuthorNames.Any(n => TokenMatch.Equal(h.Surname, PersonNames.Surname(n)))));
        return !present && CatalogQueries.HypothesisTitle(hypothesis!) is { } q ? [new CatalogRequest(CatalogSteps.Hypothesis, q)] : [];
    }

    /// <summary>The next fallback call given the current ranking, or null when retrieval is done.</summary>
    public static CatalogRequest? Next(LadderState state)
    {
        if (state.CallsRemaining <= 0) return null;
        var steps = state.StepsIssued;
        if (ActiveTopic(state) is { } topic)
        {
            // A topic request has no person or character fallbacks. If the subject search found nothing, any one of the
            // interpreter's headings is enough (still by the requested author), unless that is the same search again.
            return steps.TryGetValue(CatalogSteps.Topic, out var hits) && hits == 0 && !steps.ContainsKey(CatalogSteps.TopicAny)
                   && CatalogQueries.TopicAny(topic.Terms, topic.AuthorHint) is { } anyCall
                   && anyCall != TopicCall(topic)
                ? new CatalogRequest(CatalogSteps.TopicAny, anyCall)
                : null;
        }
        var signals = state.Ranking.Ladder;
        var h = NormalizedHypothesis.From(state.Hypothesis);
        var search = state.Query.Search;
        bool Issued(string step) => steps.ContainsKey(step);
        bool Empty(string step) => steps.TryGetValue(step, out var n) && n == 0;
        var fallbackIssued = Issued(CatalogSteps.PersonFallback);
        var authorIssued = Issued(CatalogSteps.AuthorWorks);

        // Stage 1, weak pool: a person search for the interpreter's characters (AND, then OR on 0 hits), then the OR
        // search. Entered only while the ranking is weak, and closed once a later stage has run.
        if (!fallbackIssued && !authorIssued)
        {
            if (Issued(CatalogSteps.Person))
            {
                var tokens = h is null ? [] : PersonTokens(h, search);
                if (Empty(CatalogSteps.Person) && !Issued(CatalogSteps.PersonAny) && CatalogQueries.PersonTerms(tokens).Count > 1)
                    return Request(CatalogSteps.PersonAny, () => CatalogQueries.Persons(tokens, allRequired: false));
                if (!signals.Strong && !Issued(CatalogSteps.AnyOf) && search.Count >= 2
                    && Request(CatalogSteps.AnyOf, () => CatalogQueries.AnyOf(search)) is { } anyOf)
                    return anyOf;
            }
            else if (!signals.Strong && !Issued(CatalogSteps.AnyOf))
            {
                var tokens = h is null ? [] : PersonTokens(h, search);
                if (Request(CatalogSteps.Person, () => CatalogQueries.Persons(tokens, allRequired: true)) is { } person)
                    return person;
                if (search.Count >= 2 && Request(CatalogSteps.AnyOf, () => CatalogQueries.AnyOf(search)) is { } anyOf)
                    return anyOf;
            }
        }

        // Stage 2 (DE-02): no A/B work, not author-only, no person search yet → the leftover words may be character
        // names (AND, then OR when AND found nothing; a 0-hit call leaves the pool, so the ranking and tokens are unchanged).
        if (!authorIssued)
        {
            var tokens = signals.CharacterFallbackTokens;
            if (fallbackIssued)
            {
                if (Empty(CatalogSteps.PersonFallback) && !Issued(CatalogSteps.PersonFallbackAny) && CatalogQueries.PersonTerms(tokens).Count > 1)
                    return Request(CatalogSteps.PersonFallbackAny, () => CatalogQueries.Persons(tokens, allRequired: false));
            }
            else if (!Issued(CatalogSteps.Person) && tokens.Count > 0
                     && Request(CatalogSteps.PersonFallback, () => CatalogQueries.Persons(tokens, allRequired: true)) is { } fallback)
            {
                return fallback;
            }
        }

        // Stage 3: an author-only query shows that author's most-published works (by key, else by the interpreter's author name).
        if (!authorIssued && signals.AuthorOnly)
        {
            if (signals.TopWorksAuthorKey is { } key && Request(CatalogSteps.AuthorWorks, () => CatalogQueries.AuthorTopWorks(key)) is { } byKey)
                return byKey;
            if (h?.Author is { } name && Request(CatalogSteps.AuthorWorks, () => CatalogQueries.AuthorByName(name)) is { } byName)
                return byName;
        }

        // Stage 4, R3-0195: typed title words split across two works by one author; one OR search looks for the other work.
        if (signals.MashupProbeTokens.Count > 0 && !Issued(CatalogSteps.AnyOf))
            return Request(CatalogSteps.AnyOf, () => CatalogQueries.AnyOf(signals.MashupProbeTokens));
        return null;
    }

    /// <summary>
    /// The topic request the ranking still answers as one. When the typed text is itself a book's title ("Novel on Yellow
    /// Paper") the ranker keeps the book search (no topic label), and so do the fallbacks.
    /// </summary>
    private static TopicRequest? ActiveTopic(LadderState state)
        => state.Ranking.Ranked.Count > 0 && state.Ranking.TopicLabel is null ? null : TopicRequest.From(state.Query, state.Hypothesis);

    /// <summary>
    /// The subject search, by the requested author when one is named. The interpreter's subjects: the first (most
    /// specific) only; the broader ones add ranking evidence, and requiring them all narrows the search badly. Typed
    /// phrases are all equally wanted: any one of them (subject:(bears) OR subject:(wolves)); a single phrase keeps its
    /// words together (subject:(french AND revolution)).
    /// </summary>
    private static CatalogQuery? TopicCall(TopicRequest topic)
        => topic.FromInterpreter || TopicTerms.Phrases(topic).Count == 1
            ? CatalogQueries.Topic(TopicTerms.FirstSubject(topic) is { } first ? [first] : [], topic.AuthorHint)
            : CatalogQueries.TopicAny(topic.Terms, topic.AuthorHint);

    /// <summary>A builder rejects input with no usable word (ArgumentException); then there is nothing to send.</summary>
    private static CatalogRequest? Request(string step, Func<CatalogQuery> build)
    {
        try
        {
            return new CatalogRequest(step, build());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The character-name words (minus stop words) the user typed, else the first two (the interpreter named characters
    /// the user only described). Deduped, at most 3.
    /// </summary>
    internal static IReadOnlyList<string> PersonTokens(NormalizedHypothesis h, IReadOnlyList<string> search)
    {
        var all = h.Characters.SelectMany(c => TextFolding.Tokens(c)).Where(t => !Vocabulary.Stop.Contains(t)).ToList();
        var typed = all.Where(search.Contains).ToList();
        var chosen = typed.Count > 0 ? typed : all.Take(2).ToList();
        return chosen.Distinct().Take(3).ToList();
    }

    /// <summary>The hypothesis title (or its author, when it has no title) adds a word the query lacks.</summary>
    private static bool AddsWords(CleanedQuery query, NormalizedHypothesis h)
    {
        var typed = Numerals.ToDigits([.. query.Search, .. query.All.Where(query.Optional.Contains).Distinct()]);
        IEnumerable<string> words = h.HasTitle ? h.Main : [h.Surname];
        return !words.All(typed.Contains);
    }
}

/// <param name="StepsIssued">Steps already executed, with how many works each returned (null = the call failed).</param>
/// <param name="CallsRemaining">Calls left in the budget; the ladder must not plan a retry it cannot afford.</param>
public sealed record LadderState(
    CleanedQuery Query,
    QueryHypothesis? Hypothesis,
    IReadOnlyList<CandidateWork> Pool,
    RankingResult Ranking,
    IReadOnlyDictionary<string, int?> StepsIssued,
    int CallsRemaining);
