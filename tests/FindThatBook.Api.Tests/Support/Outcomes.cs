using FindThatBook.Core.Explanations;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;
using FindThatBook.Core.Search;

namespace FindThatBook.Api.Tests.Support;

/// <summary>
/// Hand-built pipeline outcomes for the brief's scenarios, shaped like recorded Open Library data. They test the
/// HTTP mapping on its own; the ranking that would produce them is tested in Core.
/// </summary>
public static class Outcomes
{
    public static CleanedQuery Query(string raw, IReadOnlyList<string>? search = null, IReadOnlyList<string>? noise = null,
        int? year = null, params EditionIntent[] intents) => new()
        {
            Raw = raw,
            Text = raw,
            All = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Search = search ?? raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Optional = new HashSet<string>(),
            Head = [],
            Year = year,
            SoftTokens = new HashSet<string>(),
            Intents = new HashSet<EditionIntent>(intents),
            RemovedNoise = noise ?? [],
        };

    public static RankedWork Work(string id, string title, MatchTier tier, double score, IReadOnlyList<AuthorCredit> primary,
        IReadOnlyList<AuthorCredit>? contributors = null, int? year = null, int editions = 1, int? cover = null,
        MatchEvidence? evidence = null, string? subtitle = null, IReadOnlyList<string>? folded = null) => new()
        {
            Work = new CandidateWork
            {
                WorkId = id,
                Title = title,
                Subtitle = subtitle,
                AuthorNames = [.. primary.Select(a => a.Name), .. (contributors ?? []).Select(a => a.Name)],
                AuthorKeys = [.. primary.Select(a => a.Key ?? "")],
                FirstPublishYear = year,
                EditionCount = editions,
                CoverId = cover,
            },
            Tier = tier,
            Score = score,
            PrimaryAuthors = primary,
            Contributors = contributors ?? [],
            Evidence = evidence ?? new MatchEvidence(),
            FoldedWorkIds = folded ?? [],
        };

    public static SearchItem Item(int rank, RankedWork work, string explanation, ExplanationSource source = ExplanationSource.Llm, bool secondOpinion = false)
        => new(rank, work, new Explanation(explanation, source), secondOpinion);

    public static CatalogCallRecord Call(string step, int results, int numFound, long ms, int attempts = 1, bool cacheHit = false,
        CatalogCallOutcome outcome = CatalogCallOutcome.Ok)
        => new(step, "q=…", outcome, cacheHit, cacheHit ? 0 : attempts, ms,
            outcome == CatalogCallOutcome.Ok ? results : null, outcome == CatalogCallOutcome.Ok ? numFound : null, false, null);

    public static LlmCall Llm(string operation, long? input, long? output, long ms, LlmCallStatus status = LlmCallStatus.Ok, bool cacheHit = false,
        string model = "test-model", string? promptId = null)
        => new(operation, "gemini", model, promptId ?? (operation == "interpret" ? "interpret.v3" : "explain.v3"), status, cacheHit, input, output, ms);

    private static readonly AuthorCredit Tolkien = new("J.R.R. Tolkien", "OL26320A");
    private static readonly AuthorCredit Dickens = new("Charles Dickens", "OL24638A");

    /// <summary>"tolkien hobbit illustrated deluxe 1937" → The Hobbit, tier A (edition words are noise, not intents).</summary>
    public static SearchOutcome ClearMatch()
    {
        var hobbit = Work("OL27482W", "The Hobbit", MatchTier.A, 14.2, [Tolkien], [new("Alan Lee", null, "illustrator")],
            year: 1937, editions: 482, cover: 14627509,
            evidence: new MatchEvidence { ExactTitle = true, TitleScore = 1, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["hobbit"],
                Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien", YearMatched = true });
        var fellowship = Work("OL27513W", "The Fellowship of the Ring", MatchTier.D, 7.9, [Tolkien], year: 1954, editions: 319, cover: 14627060,
            evidence: new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" });
        return Build(
            Query("tolkien hobbit illustrated deluxe 1937", ["tolkien", "hobbit"], ["illustrated", "deluxe"], 1937),
            new QueryHypothesis("The Hobbit", "J.R.R. Tolkien", [], [], null, null),
            MatchStatus.Match, StatusReason.ExactTitlePrimaryAuthor, null,
            [
                Item(1, hobbit, "Exact title match; J.R.R. Tolkien is the primary author; first published 1937."),
                Item(2, fellowship, "Author-only match; J.R.R. Tolkien is the primary author; 319 editions."),
            ],
            JudgeVerdict.Off, [],
            [Call(CatalogSteps.Broad, 20, 1503, 1830)],
            [Llm("interpret", 412, 58, 610), Llm("explain", 768, 152, 290)],
            new() { ["clean"] = 0, ["interpret"] = 610, ["retrieve"] = 1830, ["rank"] = 1, ["explain"] = 290, ["judge"] = 0 },
            2140);
    }

    /// <summary>"hobbit dixon graphic novel" → the graphic novel, tier B: Dixon is an adapter, Tolkien the primary author.</summary>
    public static SearchOutcome ContributorMatch()
    {
        var graphic = Work("OL219602W", "The Hobbit", MatchTier.B, 12.4, [Tolkien],
            [new("Charles Dixon", "OL2453782A", "adapter"), new("Sean Deming", "OL758306A", "adapter"), new("David Wenzel", null, "illustrator")],
            year: 1989, editions: 4, cover: 1234567,
            evidence: new MatchEvidence { ExactTitle = true, TitleScore = 1, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["hobbit"],
                Role = AuthorRole.Contributor, MatchedAuthor = "Charles Dixon", MatchedContributorRole = "adapter", IntentMatch = true },
            subtitle: "An Illustrated Edition of the Fantasy Classic");
        var canonical = Work("OL27482W", "The Hobbit", MatchTier.E, 6.3, [Tolkien], year: 1937, editions: 482, cover: 14627509,
            evidence: new MatchEvidence { ExactTitle = true, TitleScore = 1, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["hobbit"], ForeignAuthor = true },
            folded: ["OL15361853W"]);
        return Build(
            Query("hobbit dixon graphic novel", ["hobbit", "dixon"], ["graphic", "novel"], null, EditionIntent.Adaptation),
            new QueryHypothesis("The Hobbit", "Chuck Dixon", [], ["graphic novel"], null, null),
            MatchStatus.Match, StatusReason.ExactTitleContributorAuthor, null,
            [
                Item(1, graphic, "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter of this graphic novel."),
                Item(2, canonical, "Exact title match; J.R.R. Tolkien is the primary author, but Dixon is not credited on this work.", ExplanationSource.Template),
            ],
            JudgeVerdict.Off, [],
            [Call(CatalogSteps.Broad, 20, 312, 1205), Call(CatalogSteps.Hypothesis, 14, 14, 980, attempts: 2)],
            [Llm("interpret", 405, 61, 590), Llm("explain", 820, 170, 310)],
            new() { ["clean"] = 0, ["interpret"] = 590, ["retrieve"] = 2190, ["rank"] = 1, ["explain"] = 310, ["judge"] = 0 },
            2530);
    }

    /// <summary>"dickens" → his most-published works, tier D, ambiguous with a hint.</summary>
    public static SearchOutcome AmbiguousAuthorOnly()
    {
        var author = new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Charles Dickens" };
        (string Id, string Title, int Year, int Editions, int Cover)[] works =
        [
            ("OL1440354W", "A Christmas Carol", 1843, 3196, 13299306),
            ("OL118421W", "Oliver Twist", 1838, 2181, 14361217),
            ("OL118411W", "A Tale of Two Cities", 1859, 1971, 13301713),
            ("OL118435W", "Great Expectations", 1860, 1616, 13285640),
            ("OL118445W", "David Copperfield", 1849, 1310, 12711497),
        ];
        var items = works.Select((w, i) => Item(i + 1,
                Work(w.Id, w.Title, MatchTier.D, 8 - i * 0.1, [Dickens], year: w.Year, editions: w.Editions, cover: w.Cover, evidence: author),
                $"Author-only match: Charles Dickens is the primary author; {w.Editions:N0} editions."))
            .ToList();
        return Build(
            Query("dickens"),
            new QueryHypothesis(null, "Charles Dickens", [], [], null, null),
            MatchStatus.Ambiguous, StatusReason.AuthorOnly, "These are Charles Dickens's most-published works. Add a title to narrow it.",
            items,
            JudgeVerdict.Skipped("not_applicable"), [],
            [Call(CatalogSteps.Broad, 20, 1280, 2410), Call(CatalogSteps.AuthorWorks, 10, 1280, 1650)],
            [Llm("interpret", 380, 22, 540), Llm("explain", 1110, 240, 420)],
            new() { ["clean"] = 0, ["interpret"] = 540, ["retrieve"] = 4060, ["rank"] = 2, ["explain"] = 420, ["judge"] = 0 },
            4510);
    }

    /// <summary>Nothing usable: a 200 with no candidates and a hint.</summary>
    public static SearchOutcome None(string raw = "xqzv blorptangle") => Build(
        Query(raw),
        null,
        MatchStatus.None, StatusReason.NoCandidates, "No Open Library work matched. Check the spelling or add an author.",
        [],
        JudgeVerdict.Skipped("no_candidates"), [],
        [Call(CatalogSteps.Broad, 0, 0, 1320), Call(CatalogSteps.AnyOf, 0, 0, 1105)],
        [Llm("interpret", 371, 18, 480)],
        new() { ["clean"] = 0, ["interpret"] = 480, ["retrieve"] = 2425, ["rank"] = 0 },
        2440);

    /// <summary>"mark huckleberry" with the LLM down (429): heuristic parse + template sentences, still a useful answer.</summary>
    public static SearchOutcome DegradedLlmDown()
    {
        var huck = Work("OL53908W", "Adventures of Huckleberry Finn", MatchTier.C, 9.8, [new("Mark Twain", "OL18319A")], year: 1876,
            editions: 2623, cover: 8231994,
            evidence: new MatchEvidence { TitleScore = 0.5, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["huckleberry"],
                Role = AuthorRole.Primary, MatchedAuthor = "Mark Twain" });
        return Build(
            Query("mark huckleberry"),
            null,
            MatchStatus.Ambiguous, StatusReason.NearTitleAuthor, "Add more of the title or the author's full name.",
            [Item(1, huck, "Title contains \"huckleberry\"; Mark Twain is the primary author; 2,623 editions.", ExplanationSource.Template)],
            JudgeVerdict.Off, [DegradedReason.InterpretationFallback, DegradedReason.ExplanationFallback],
            [Call(CatalogSteps.Broad, 20, 214, 1730)],
            [Llm("interpret", null, null, 212, LlmCallStatus.RateLimited), Llm("explain", null, null, 0, LlmCallStatus.BudgetExhausted)],
            new() { ["clean"] = 0, ["interpret"] = 212, ["retrieve"] = 1730, ["rank"] = 1, ["explain"] = 0, ["judge"] = 0 },
            1760);
    }

    /// <summary>"find me a book about bears" → books about bears: ambiguous, reason topic_search, tier e with subject evidence.</summary>
    /// <remarks>Work ids here are synthetic (OL9000…W); the live example in the OpenAPI document carries real ones.</remarks>
    public static SearchOutcome TopicSearch()
    {
        var suggestions = new[] { new BookSuggestion("Little Bear", "Else Holmelund Minarik"), new BookSuggestion("A Bear Called Paddington", "Michael Bond") };
        MatchEvidence Subjects(params string[] subjects) => new() { MatchedSubjects = subjects };
        var littleBear = Work("OL900001W", "Little Bear", MatchTier.E, 6.2, [new("Else Holmelund Minarik", "OL900001A")],
            [new("Maurice Sendak", "OL900002A", "illustrator")], year: 1957, editions: 87, cover: 900001,
            evidence: Subjects("Bears", "Teddy bears", "Bears -- Fiction", "Mothers and sons") with { SuggestedByInterpreter = true });
        var paddington = Work("OL900002W", "A Bear Called Paddington", MatchTier.E, 5.9, [new("Michael Bond", "OL900003A")],
            year: 1958, editions: 112, cover: 900002, evidence: Subjects("Bears", "Paddington Bear (Fictitious character)") with { SuggestedByInterpreter = true });
        var bearHunt = Work("OL900003W", "We're Going on a Bear Hunt", MatchTier.E, 5.1, [new("Michael Rosen", "OL900004A")],
            [new("Helen Oxenbury", "OL900005A", "illustrator")], year: 1989, editions: 64, cover: 900003, evidence: Subjects("Bears"));
        var query = Query("find me a book about bears", ["bears"]) with { RequestTokens = new HashSet<string> { "find", "me" }, Topic = ["bears"] };
        return Build(
            query,
            new QueryHypothesis(null, null, [], [], null, null) { Intent = QueryIntent.Topic, Subjects = ["bears"], Suggestions = suggestions },
            MatchStatus.Ambiguous, StatusReason.TopicSearch, "Add a title or author to find a specific one.",
            [
                Item(1, littleBear, "Topic match: Open Library lists the subjects Bears and Teddy bears; Else Holmelund Minarik is the primary author; 87 editions."),
                Item(2, paddington, "Topic match: Open Library lists the subject Bears; Michael Bond is the primary author; 112 editions."),
                Item(3, bearHunt, "Topic match: Open Library lists the subject Bears; Michael Rosen is the primary author; 64 editions.", ExplanationSource.Template),
            ],
            JudgeVerdict.Off, [],
            [Call(CatalogSteps.Broad, 20, 2210, 1840), Call(CatalogSteps.Topic, 20, 15119, 2120), Call(CatalogSteps.Suggestions, 2, 2, 1310)],
            [Llm("interpret", 520, 96, 700, promptId: "interpret.v4"), Llm("explain", 980, 190, 420)],
            new() { ["clean"] = 0, ["interpret"] = 700, ["retrieve"] = 3430, ["rank"] = 2, ["explain"] = 420, ["judge"] = 0 },
            3870,
            new TopicRequest(["bears"], null, suggestions));
    }

    /// <summary>A topic nothing in Open Library is about ("a book about xqzv"), LLM off: none, and still no candidates.</summary>
    public static SearchOutcome TopicNone()
    {
        var query = Query("a book about xqzv", ["xqzv"]) with { Topic = ["xqzv"] };
        return Build(
            query, null,
            MatchStatus.None, StatusReason.NoCandidates, "Try a broader topic, or add a title or author.",
            [],
            JudgeVerdict.Off, [DegradedReason.AiNotConfigured],
            [Call(CatalogSteps.Broad, 0, 0, 1100), Call(CatalogSteps.Topic, 0, 0, 980), Call(CatalogSteps.TopicAny, 0, 0, 940)],
            [Llm("interpret", null, null, 0, LlmCallStatus.NotConfigured)],
            new() { ["clean"] = 0, ["interpret"] = 0, ["retrieve"] = 3020, ["rank"] = 0 },
            3030,
            new TopicRequest(["xqzv"], null, []));
    }

    public static SearchOutcome Build(CleanedQuery query, QueryHypothesis? hypothesis, MatchStatus status, StatusReason reason, string? hint,
        IReadOnlyList<SearchItem> items, JudgeVerdict judge, IReadOnlyList<DegradedReason> degraded,
        IReadOnlyList<CatalogCallRecord> calls, IReadOnlyList<LlmCall> llm, Dictionary<string, long> timings, long elapsed,
        TopicRequest? topic = null) => new()
        {
            Topic = topic,
            Query = query,
            Hypothesis = hypothesis,
            InterpretationSource = hypothesis is null ? InterpretationSource.Heuristic : InterpretationSource.Llm,
            Status = status,
            Reason = reason,
            Hint = hint,
            Items = items,
            Judge = judge,
            Degraded = degraded,
            Trace = new SearchTrace(elapsed, timings, calls, llm, 5),
            Ranking = new RankingResult([.. items.Select(i => i.Work)], status, reason),
        };
}
