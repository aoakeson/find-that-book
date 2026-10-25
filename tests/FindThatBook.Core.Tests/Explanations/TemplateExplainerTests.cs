using FindThatBook.Core.Explanations;
using FindThatBook.Core.Model;
using static FindThatBook.Core.Tests.Explanations.ExplanationFixtures;

namespace FindThatBook.Core.Tests.Explanations;

public class TemplateExplainerTests
{
    private static string Template(RankedWork work, CleanedQuery? query = null, QueryHypothesis? hypothesis = null) =>
        TemplateExplainer.Explain(ExplanationFactsBuilder.Build(work, query ?? Query("q"), hypothesis));

    [Fact]
    public void Explain_BriefScenarioA_TypedYearMatchesOpenLibrarysYear() =>
        Template(HobbitA() with { Evidence = HobbitA().Evidence with { YearMatched = true } }, Query("tolkien hobbit illustrated deluxe 1937", year: 1937))
            .ShouldBe("Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches the year in the search.");

    [Fact]
    public void Explain_NoYearTyped_StatesNoYear_DA061() =>
        Template(HobbitA(), Query("the hobbit")).ShouldBe("Exact title match; J.R.R. Tolkien is the primary author.");

    [Fact]
    public void Explain_BriefScenarioB_ContributorIsLowerSignalWithRole() =>
        Template(HobbitB(), Query("hobbit dixon"))
            .ShouldBe("Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon and Sean Deming are listed as adapters.");

    public static TheoryData<string, string> Scenarios => new()
    {
        // C: near title + author.
        // An editor of some edition does not explain the match: not named.
        { "near_title", "Near title match on the word “hobbit”; J.R.R. Tolkien is the primary author." },
        // D: author-only, most-published first; the 1800 sentinel year is not stated (DA-061).
        { "author_only", "Author-only match; Charles Dickens is the primary author; 3,196 editions." },
        { "contributor_only", "Contributor-only match; Roald Dahl is the primary author; Quentin Blake is listed as illustrator; 300 editions." },
        // E variants.
        { "character", "Character match on Elizabeth Bennet; Jane Austen is the primary author; 4,042 editions." },
        { "partial_title", "Partial title match on the word “huckleberry”; Mark Twain is the primary author; 2,623 editions." },
        { "title_only", "Exact title, but not by the searched author; Jude Fisher is the primary author; 3 editions." },
        { "weak", "Weak match; Jane Doe is the primary author; 2 editions." },
        // DE-06: a translated work matched through its English edition title.
        { "translation", "Exact title match; known in English as The Three-Body Problem; 刘慈欣 is the primary author." },
        // DA-007: pen name typed.
        { "alias", "Author-only match; Stephen King is the primary author, also listed as Richard Bachman; 60 editions." },
        // Edition intent satisfied (the query asked for an adaptation).
        { "intent", "Exact title match; matches the requested adaptation; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter." },
        { "inferred", "Interpreted title match; Jane Austen is the primary author; Elizabeth Bennet is a listed character." },
        { "year_matched", "Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches the year in the search." },
        // A typed year that matches the 1800 sentinel is still not stated.
        { "year_sentinel", "Author-only match; Charles Dickens is the primary author; 3,196 editions." },
        { "duplicates", "Exact title match; Jane Austen is the primary author; 55 duplicate records merged." },
        { "no_author", "Exact title match; Open Library lists no primary author." },
        // Topic requests.
        { "topic", "Topic match: Open Library lists the subjects Bears and Teddy bears; A. A. Milne is the primary author; 87 editions." },
        { "topic_long_heading", "Topic match: Open Library lists the subject Bears; Michael Rosen is the primary author; 60 editions." },
        { "topic_one_subject", "Topic match: Open Library lists the subject Bears; Else Holmelund Minarik is the primary author; 40 editions." },
        { "suggested", "Suggested for this topic; Michael Bond is the primary author; 120 editions." },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Explain_Scenario_CitesTheFacts(string scenario, string expected)
    {
        var (work, query, hypothesis) = ScenarioInput(scenario);
        var facts = ExplanationFactsBuilder.Build(work, query, hypothesis);
        var sentence = TemplateExplainer.Explain(facts);
        sentence.ShouldBe(expected);
        GroundingCheck.Check(sentence, facts).Problems.ShouldBeEmpty();
    }

    private static (RankedWork, CleanedQuery, QueryHypothesis?) ScenarioInput(string scenario) => scenario switch
    {
        "near_title" => (Work("OL1W", "The Annotated Hobbit", MatchTier.C, ["J.R.R. Tolkien"], [("Douglas A. Anderson", "Editor")], 1988, 30,
            new MatchEvidence { TitleScore = 0.7, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary }), Query("hobbit tolkien"), null),
        "author_only" => (Work("OL118421W", "A Tale of Two Cities", MatchTier.D, ["Charles Dickens"], year: 1800, editions: 3196,
            evidence: new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Charles Dickens" }), Query("dickens"), null),
        "contributor_only" => (Work("OL45846W", "Matilda", MatchTier.D, ["Roald Dahl"], [("Quentin Blake", "Illustrator")], 1988, 300,
            new MatchEvidence { Role = AuthorRole.Contributor, MatchedAuthor = "Quentin Blake", MatchedContributorRole = "Illustrator" }), Query("quentin blake"), null),
        "character" => (Work("OL66554W", "Pride and Prejudice", MatchTier.E, ["Jane Austen"], year: 1813, editions: 4042,
            evidence: new MatchEvidence { CharacterCoverage = 1, MatchedCharacters = ["elizabeth bennet"] },
            persons: ["Mr. Darcy", "Elizabeth Bennet", "Jane Bennet"]), Query("elizabeth bennet"), null),
        "partial_title" => (Work("OL53908W", "Adventures of Huckleberry Finn", MatchTier.E, ["Mark Twain"], year: 1876, editions: 2623,
            evidence: new MatchEvidence { TitleScore = 0.4, MatchedTitleTerms = ["huckleberry", "huckelbery"] }), Query("huckleberry huckelbery"), null),
        "title_only" => (Work("OL20W", "The Hobbit", MatchTier.E, ["Jude Fisher"], year: 2012, editions: 3,
            evidence: new MatchEvidence { ExactTitle = true, TitleScore = 1, MatchedTitleTerms = ["hobbit"], ForeignAuthor = true }), Query("hobbit dixon"), null),
        "weak" => (Work("OL21W", "Collected Letters", MatchTier.E, ["Jane Doe"], editions: 2, evidence: new MatchEvidence()), Query("letters"), null),
        "translation" => (Work("OL17267881W", "三体", MatchTier.A, ["刘慈欣"], year: 2006, editions: 45,
            evidence: new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "The Three-Body Problem", MatchedTitleTerms = ["three", "body", "problem"] }),
            Query("the three body problem"), null),
        "alias" => (Work("OL81634W", "The Long Walk", MatchTier.D, ["Stephen King"], year: 1979, editions: 60,
            evidence: new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Stephen King", AuthorViaAlternativeName = true },
            alternativeNames: ["Stephen Edwin King", "Richard Bachman", "Steve King"]), Query("richard bachman"), null),
        "intent" => (Work("OL219602W", "The Hobbit", MatchTier.B, ["J.R.R. Tolkien"], [("Charles Dixon", "Adapter")], 1990, 10,
            new MatchEvidence { ExactTitle = true, IntentMatch = true, Role = AuthorRole.Contributor, MatchedAuthor = "Charles Dixon", MatchedContributorRole = "Adapter" }),
            Query("hobbit dixon graphic novel", intents: new HashSet<EditionIntent> { EditionIntent.Adaptation }), null),
        "inferred" => (Work("OL66554W", "Pride and Prejudice", MatchTier.A, ["Jane Austen"], year: 1813, editions: 4042,
            evidence: new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.Hypothesis, Role = AuthorRole.Primary, MatchedCharacters = ["bennet"] },
            persons: ["Elizabeth Bennet", "Fitzwilliam Darcy"]),
            Query("austen bennet"), new QueryHypothesis("Pride and Prejudice", "Jane Austen", ["bennet"], [], null, null)),
        "year_matched" => (HobbitA() with { Evidence = HobbitA().Evidence with { YearMatched = true } }, Query("tolkien hobbit 1937", year: 1937), null),
        "year_sentinel" => (Work("OL118421W", "A Tale of Two Cities", MatchTier.D, ["Charles Dickens"], year: 1800, editions: 3196,
            evidence: new MatchEvidence { Role = AuthorRole.Primary, YearMatched = true }), Query("dickens 1800"), null),
        "duplicates" => (Work("OL66554W", "Pride and Prejudice", MatchTier.A, ["Jane Austen"], year: 1813, editions: 4042,
            folded: Enumerable.Range(1, 55).Select(i => $"OL{i}W").ToArray()), Query("pride and prejudice"), null),
        "no_author" => (Work("OL30W", "Beowulf", MatchTier.A, [], year: 0, editions: 900), Query("beowulf"), null),
        "topic" => (Work("OL262758W", "Winnie-the-Pooh", MatchTier.E, ["A. A. Milne"], editions: 87,
            evidence: new MatchEvidence { MatchedSubjects = ["Bears", "Teddy bears"], SuggestedByInterpreter = true }), Query("find me a book about bears", topic: ["bears"]), null),
        "topic_long_heading" => (Work("OL3W", "We're Going on a Bear Hunt", MatchTier.E, ["Michael Rosen"], editions: 60,
            evidence: new MatchEvidence { MatchedSubjects = ["Picture books for children -- Animals of the forest and the river -- Bears", "Toys"] }),
            Query("books about bears", topic: ["bears"]), null),
        "topic_one_subject" => (Work("OL1W", "Little Bear", MatchTier.E, ["Else Holmelund Minarik"], editions: 40,
            evidence: new MatchEvidence { MatchedSubjects = ["Bears"] }), Query("books about bears"), null),
        "suggested" => (Work("OL2W", "A Bear Called Paddington", MatchTier.E, ["Michael Bond"], editions: 120,
            evidence: new MatchEvidence { SuggestedByInterpreter = true }), Query("books about bears"), null),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    /// <summary>
    /// The template must pass the grounding check for every tier/role/evidence combination, including awkward data:
    /// initials, diacritics, other scripts, years and punctuation in titles, MARC-style names, unknown roles, long strings.
    /// </summary>
    [Fact]
    public void Explain_EveryGeneratedCombination_PassesGroundingAndFits()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (work, query, hypothesis) in Generated())
        {
            count++;
            var facts = ExplanationFactsBuilder.Build(work, query, hypothesis);
            var sentence = TemplateExplainer.Explain(facts);
            var result = GroundingCheck.Check(sentence, facts);
            if (!result.Passed || sentence.Length > GroundingCheck.MaxLength)
                failures.Add($"{facts.Kind} {sentence.Length}: {sentence} → {string.Join(", ", result.Problems)}");
        }
        count.ShouldBeGreaterThan(5000);
        failures.Take(10).ShouldBeEmpty();
    }

    private static readonly string[] Titles =
    [
        "The Hobbit", "Complete Poems, 1904-1962", "2001: A Space Odyssey", "Who's Afraid of Virginia Woolf?", "Oh, the Places You'll Go!",
        "三体", "Война и мир", "Cien años de soledad", "Dr. Jekyll and Mr. Hyde", "11/22/63", "Catch-22", "HERmione",
        "The Fellowship of the Ring: Being the First Part of The Lord of the Rings, With a Very Long Subtitle About Maps and Appendices Included",
    ];

    private static readonly string[][] PrimarySets =
    [
        [], ["J.R.R. Tolkien"], ["E. E. Cummings", "C. S. Lewis"], ["bell hooks"], ["Gabriel García Márquez"], ["刘慈欣"],
        ["Tolkien, J. R. R. 1892-1973"], ["Martin Luther King Jr."], ["H.D."],
        ["Steven D. Levitt", "Stephen J. Dubner", "A Third Author With An Extraordinarily Long Name For Testing Caps"],
    ];

    private static readonly (string, string?)[][] ContributorSets =
    [
        [], [("Charles Dixon", "Adapter")], [("Charles Dixon", "Adapter"), ("Sean Deming", "Adapter")],
        [("Alan Lee", "Illustrator"), ("Christopher Tolkien", "Editor"), ("Douglas A. Anderson", "annotations")],
        [("Kemble, E. W. 1861-1933", "ill."), ("Copyright Paperback Collection", "Library of Congress")],
        [("Rosalyn Landor", "Narrator"), ("Shao jia yun", "Translator"), ("Some Person", null), ("A Fourth", "Foreword")],
    ];

    private static IEnumerable<(RankedWork, CleanedQuery, QueryHypothesis?)> Generated()
    {
        var tiers = Enum.GetValues<MatchTier>();
        var sources = Enum.GetValues<TitleSource>();
        int?[] years = [null, 1937, 1800, 0, -5];
        var query = Query("hobbit tolkien dixon richard bachman graphic", intents: new HashSet<EditionIntent> { EditionIntent.Adaptation });
        var hypothesis = new QueryHypothesis("The Hobbit", "J.R.R. Tolkien", [], [], "The Three-Body Problem", null);
        var i = 0;
        foreach (var title in Titles)
        foreach (var primaries in PrimarySets)
        foreach (var contributors in ContributorSets)
        foreach (var tier in tiers)
        foreach (var _ in Enumerable.Range(0, 3))
        {
            i++;
            var contributorMatch = i % 3 == 0 && contributors.Length > 0;
            var evidence = new MatchEvidence
            {
                ExactTitle = tier is MatchTier.A or MatchTier.B || i % 7 == 0,
                TitleScore = i % 4 * 0.3,
                TitleSource = sources[i % sources.Length],
                MatchedTitleTerms = ExplanationTextTokens(title).Take(i % 4).Append("injected").ToList(),
                Role = contributorMatch ? AuthorRole.Contributor : primaries.Length > 0 && i % 2 == 0 ? AuthorRole.Primary : null,
                MatchedAuthor = contributorMatch ? contributors[0].Item1 : primaries.FirstOrDefault(),
                MatchedContributorRole = contributorMatch ? contributors[0].Item2 : null,
                AuthorViaAlternativeName = i % 5 == 0,
                MatchedCharacters = i % 6 == 0 ? ["bilbo", "Gandalf the Grey"] : [],
                IntentMatch = i % 8 == 0,
                YearMatched = i % 2 == 1,
                MatchedEditionTitle = i % 9 == 0 ? "The Three-Body Problem" : i % 10 == 0 ? title : null,
                ForeignAuthor = i % 11 == 0,
                MatchedSubjects = i % 4 == 0 ? ["Bears -- Juvenile fiction", "Teddy bears", "Toys"] : i % 17 == 0 ? ["Visit www dot evil", "Bears"] : [],
                SuggestedByInterpreter = i % 5 == 1,
            };
            yield return (Work($"OL{i}W", title, tier, primaries, contributors, years[i % years.Length], i * 37 % 5000, evidence,
                persons: ["Bilbo Baggins", "Gandalf the Grey"], alternativeNames: ["Richard Bachman"], folded: i % 13 == 0 ? ["OLxW"] : []),
                query, i % 2 == 0 ? hypothesis : null);
        }
    }

    private static IEnumerable<string> ExplanationTextTokens(string title) =>
        title.ToLowerInvariant().Split([' ', ',', ':', '-', '?', '!', '.', '/'], StringSplitOptions.RemoveEmptyEntries);
}
