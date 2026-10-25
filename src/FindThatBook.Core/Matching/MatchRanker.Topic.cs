using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// Topic requests ("find me a book about bears"). Topic mode applies unless the typed text is itself a book's title
    /// (<see cref="TypedTitleWorks"/>); then it is a book search and the works with that exact title come first. In topic
    /// mode a work's evidence is its subject headings (and whether it is a book the interpreter suggested), so the order
    /// is: works with topic evidence first, then the topic score, then editions, then work id. Topic score =
    /// 4.0 × topic match + 1.0 × share of the broader interpreter topics matched + 2.0 if suggested + 2.0 × author role
    /// (a typed or hinted author) + 1.0 if a heading holds a preferred audience or form ("juvenile", "fiction") + the
    /// popularity terms (2.5 × log10(1 + editions) + 0.5 × log10(1 + reading log)) − marker penalties − 3.0 when the
    /// work is by another author the query names. Topic match: for the interpreter, its first (most specific) subject;
    /// for typed phrases, all of them equally (the mean). A phrase scores 1 when one heading holds all its words, else
    /// half the share of its words some heading holds. Title similarity is left out: a title containing "bear" says
    /// little about what a book is about.
    /// </summary>
    private static void ApplyTopicOrder(List<Candidate> works, RankingContext context)
    {
        var titled = TypedTitleWorks(works, context);
        if (titled.Count > 0)
        {
            foreach (var work in works) work.OrderGroup = titled.Contains(work) ? 0 : 1;
            return;
        }
        context.TopicMode = true;
        var topic = context.Topic!;
        var phrases = TopicTerms.Phrases(topic);
        var preferences = topic.PreferredSubjects.Select(p => TextFolding.Tokens(p)).Where(p => p.Count > 0).ToList();
        foreach (var work in works)
        {
            var headings = work.Work.Subjects.Select(s => (Text: s, Forms: TopicTerms.HeadingForms(s))).ToList();
            var (match, broader) = SubjectEvidence(work, headings, phrases, topic.FromInterpreter);
            work.SuggestedByInterpreter = topic.Suggestions.Any(s => IsSuggestion(work, s));
            var preferred = preferences.Any(p => headings.Any(h => p.All(w => TopicTerms.InHeading(w, h.Forms))));
            var roleWeight = work.Role switch { AuthorRole.Primary => 1.0, AuthorRole.Contributor => 0.5, _ => 0.0 };
            work.Score = Weights.TopicSubjects * match
                         + Weights.TopicSecondarySubjects * broader
                         + Weights.TopicSuggestion * (work.SuggestedByInterpreter ? 1.0 : 0.0)
                         + Weights.AuthorRole * roleWeight
                         + Weights.TopicPreference * (preferred ? 1.0 : 0.0)
                         + Weights.Editions * Math.Log10(1.0 + work.Editions)
                         + Weights.ReadingLog * Math.Log10(1.0 + work.ReadingLog)
                         - MarkerPenalty(work.Penalties)
                         - Weights.OtherAuthorPenalty * (work.Foreign ? 1.0 : 0.0);
            work.OrderGroup = HasTopicEvidence(work) ? 0 : 1;
        }
    }

    private static bool HasTopicEvidence(Candidate work) => work.MatchedSubjects.Count > 0 || work.SuggestedByInterpreter;

    /// <summary>
    /// A topic result lists only books backed by evidence (a matched subject heading or a suggested book the catalog has)
    /// and, when the request names an author ("books about cats by doris lessing"), only that author's books, whichever
    /// call found them.
    /// </summary>
    private static bool ShownForTopic(Candidate work, RankingContext context)
        => HasTopicEvidence(work) && (context.Topic!.AuthorHint is not { } author || ByAuthor(work, author));

    /// <summary>
    /// Works whose title is the whole typed text ("Novel on Yellow Paper", which the cleaner reads as a topic frame), not
    /// counting the request's author words ("… by Stevie Smith") or the words this work's own authors explain. Such a
    /// query is a book search: the frame words left the search text, so a famous partial title (The Yellow Wallpaper,
    /// 721 editions) would otherwise outscore the title the user typed in full.
    /// </summary>
    private static HashSet<Candidate> TypedTitleWorks(List<Candidate> works, RankingContext context)
    {
        var authorWords = context.Topic!.AuthorHint is { } hint ? TextFolding.Tokens(hint).ToHashSet(StringComparer.Ordinal) : [];
        var typed = TextFolding.Content(context.Query.All).Where(t => !authorWords.Contains(t)).ToList();
        var titled = new HashSet<Candidate>();
        foreach (var work in works)
        {
            var words = typed.Where(t => !work.AuthorTokens.Contains(t)).ToList();
            if (words.Count > 0 && (TokenMatch.Score(words, work.Main).Exact || TokenMatch.Score(words, work.Full).Exact))
                titled.Add(work);
        }
        return titled;
    }

    /// <summary>The topic as named to the user; "" when the request names none.</summary>
    private static string TopicLabel(RankingContext context) => TopicTerms.Label(context.Topic!);

    /// <summary>
    /// Subject evidence. A phrase is matched when one heading holds all its words (whole words, singular or plural);
    /// different phrases may be matched by different headings ("Bears", "Wolves" cover "bears and wolves"). The headings
    /// that match become the work's matched subjects. Returns the topic match (the first interpreter subject, or the
    /// mean over typed phrases; a phrase no heading holds whole scores half the share of its words found) and the share
    /// of the broader interpreter subjects matched.
    /// </summary>
    private static (double Match, double Broader) SubjectEvidence(Candidate work, List<(string Text, HashSet<string> Forms)> headings,
        List<IReadOnlyList<string>> phrases, bool fromInterpreter)
    {
        work.MatchedSubjects = [];
        work.SubjectCoverage = 0;
        if (phrases.Count == 0 || headings.Count == 0) return (0, 0);
        var scores = new double[phrases.Count];
        var matchedPhrases = 0;
        for (var p = 0; p < phrases.Count; p++)
        {
            var words = phrases[p];
            var holding = headings.Where(h => words.All(w => TopicTerms.InHeading(w, h.Forms))).Select(h => h.Text).ToList();
            if (holding.Count == 0)
            {
                scores[p] = 0.5 * words.Count(w => headings.Any(h => TopicTerms.InHeading(w, h.Forms))) / words.Count;
                continue;
            }
            scores[p] = 1.0;
            matchedPhrases++;
            foreach (var text in holding)
                if (work.MatchedSubjects.Count < 5 && !work.MatchedSubjects.Contains(text, StringComparer.OrdinalIgnoreCase))
                    work.MatchedSubjects.Add(text);
        }
        work.SubjectCoverage = (double)matchedPhrases / phrases.Count;
        if (!fromInterpreter) return (scores.Average(), 0);
        var broader = phrases.Count == 1 ? 0.0 : (double)scores.Skip(1).Count(s => s == 1.0) / (phrases.Count - 1);
        return (scores[0], broader);
    }

    /// <summary>This record is the suggested book: the suggested title matches its title, and the suggested author (if any) is its author.</summary>
    private static bool IsSuggestion(Candidate work, BookSuggestion suggestion)
    {
        var title = TitleText.Sets(suggestion.Title).Main;
        if (title.Count == 0) return false;
        var titleMatches = TokenMatch.Score(title, work.Main).Exact || TokenMatch.Score(title, work.Full).Exact
                           || TokenMatch.FuzzyComplete(title, work.Main);
        if (!titleMatches) return false;
        return string.IsNullOrWhiteSpace(suggestion.Author) || ByAuthor(work, suggestion.Author);
    }

    /// <summary>The work is by this person: an author (or a person alias of one) has the same surname.</summary>
    private static bool ByAuthor(Candidate work, string name)
    {
        var surname = PersonNames.Surname(name);
        return surname.Length == 0
               || work.Authors.Any(a => TokenMatch.Equal(PersonNames.Surname(a.Display), surname))
               || work.AlternativeNames.Any(n => IsPersonAlias(n) && TokenMatch.Equal(PersonNames.Surname(n), surname));
    }
}
