using FindThatBook.Core.Explanations;
using FindThatBook.Core.Model;

namespace FindThatBook.Core.Tests.Explanations;

/// <summary>Hand-built ranker output and facts (independent of the real ranker and cleaner).</summary>
internal static class ExplanationFixtures
{
    public static CleanedQuery Query(string raw, IReadOnlyList<string>? search = null, IReadOnlySet<EditionIntent>? intents = null, int? year = null,
        IReadOnlyList<string>? topic = null)
    {
        var tokens = raw.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new CleanedQuery
        {
            Raw = raw,
            Text = raw,
            All = tokens,
            Search = search ?? tokens,
            Optional = new HashSet<string>(),
            Head = [],
            Year = year,
            SoftTokens = new HashSet<string>(),
            Intents = intents ?? new HashSet<EditionIntent>(),
            RemovedNoise = [],
            Topic = topic ?? [],
        };
    }

    public static RankedWork Work(
        string id,
        string title,
        MatchTier tier,
        string[] primaries,
        (string Name, string? Role)[]? contributors = null,
        int? year = null,
        int editions = 1,
        MatchEvidence? evidence = null,
        string[]? persons = null,
        string[]? alternativeNames = null,
        string[]? folded = null) => new()
    {
        Work = new CandidateWork
        {
            WorkId = id,
            Title = title,
            AuthorNames = primaries,
            FirstPublishYear = year,
            EditionCount = editions,
            Persons = persons ?? [],
            AuthorAlternativeNames = alternativeNames ?? [],
        },
        Tier = tier,
        Score = 1,
        PrimaryAuthors = primaries.Select(p => new AuthorCredit(p, null)).ToList(),
        Contributors = (contributors ?? []).Select(c => new AuthorCredit(c.Name, null, c.Role)).ToList(),
        Evidence = evidence ?? new MatchEvidence { ExactTitle = tier is MatchTier.A or MatchTier.B, TitleSource = TitleSource.Typed },
        FoldedWorkIds = folded ?? [],
    };

    // The brief's examples, as the ranker would hand them over.
    public static RankedWork HobbitA() => Work("OL27482W", "The Hobbit", MatchTier.A, ["J.R.R. Tolkien"], year: 1937, editions: 482,
        evidence: new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" });

    public static RankedWork HobbitB() => Work("OL219602W", "The Hobbit", MatchTier.B, ["J.R.R. Tolkien"],
        [("Charles Dixon", "Adapter"), ("Sean Deming", "Adapter")], year: 1990, editions: 10,
        evidence: new MatchEvidence
        {
            ExactTitle = true, TitleSource = TitleSource.Typed, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Contributor,
            MatchedAuthor = "Charles Dixon", MatchedContributorRole = "Adapter",
        });

    /// <summary>
    /// Facts in the shapes of the recorded AI-28 probe, rebuilt with code-owned phrases.
    /// A year here means the query typed that year and it matched (the only case a sentence may state one).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ExplanationFacts> Probe = new Dictionary<string, ExplanationFacts>
    {
        ["hobbit_a"] = F("OL27482W", "The Hobbit", MatchKind.ExactTitle, ["J.R.R. Tolkien"], 1937, 482, ["hobbit"], [("Alan Lee", "illustrator")]),
        ["hobbit_a_no_year"] = F("OL27482W", "The Hobbit", MatchKind.ExactTitle, ["J.R.R. Tolkien"], null, 482, ["hobbit"]),
        ["hobbit_a_plain"] = F("OL27482W", "The Hobbit", MatchKind.ExactTitle, ["J.R.R. Tolkien"], 1937, 482, ["hobbit"]),
        ["hobbit_b"] = F("OL219602W", "The Hobbit", MatchKind.ExactTitle, ["J.R.R. Tolkien"], 1990, 10, ["hobbit"],
            [("Charles Dixon", "adapter"), ("Sean Deming", "adapter")], matchedContributor: "Charles Dixon"),
        ["hobbit_b_intent"] = F("OL219602W", "The Hobbit", MatchKind.ExactTitle, ["J.R.R. Tolkien"], 1990, 10, ["hobbit"],
            [("Charles Dixon", "adapter")], matchedContributor: "Charles Dixon") with { Intent = EditionIntent.Adaptation },
        ["fellowship_d"] = F("OL27513W", "The Fellowship of the Ring", MatchKind.AuthorOnly, ["J.R.R. Tolkien"], 1954, 300),
        ["annotated_c"] = F("OL1W", "The Annotated Hobbit", MatchKind.NearTitle, ["J.R.R. Tolkien"], 1988, 30, ["hobbit"], [("Douglas A. Anderson", "editor")]),
        ["two_towers_d"] = F("OL27479W", "The Two Towers", MatchKind.AuthorOnly, ["J.R.R. Tolkien"], 1954, 250),
        ["hooks_d"] = F("OL2W", "All About Love", MatchKind.AuthorOnly, ["bell hooks"], 1999, 40),
        ["earthsea_d"] = F("OL3W", "A Wizard of Earthsea", MatchKind.AuthorOnly, ["Ursula K. Le Guin"], 1968, 200),
        ["solitude_c"] = F("OL4W", "Cien años de soledad", MatchKind.NearTitle, ["Gabriel García Márquez"], 1967, 500, ["solitude"])
            with { AlsoKnownAs = "One Hundred Years of Solitude", AlsoKnownAsLabel = "known in English as" },
        ["cummings_d"] = F("OL5W", "Complete Poems, 1904-1962", MatchKind.AuthorOnly, ["E. E. Cummings"], 1991, 20),
        ["lewis_d"] = F("OL6W", "The Lion, the Witch and the Wardrobe", MatchKind.AuthorOnly, ["C. S. Lewis"], 1950, 600),
        ["orwell_a"] = F("OL7W", "Nineteen Eighty-Four", MatchKind.ExactTitle, ["George Orwell"], 1949, 900),
        ["clarke_a"] = F("OL8W", "2001: A Space Odyssey", MatchKind.ExactTitle, ["Arthur C. Clarke"], 1968, 150, ["2001"]),
        ["dickens_d"] = F("OL9W", "A Christmas Carol", MatchKind.AuthorOnly, ["Charles Dickens"], 1843, 3196),
        ["bradbury_c"] = F("OL10W", "Fahrenheit 451", MatchKind.NearTitle, ["Ray Bradbury"], 1953, 700, ["451"]),
        ["heller_c"] = F("OL11W", "Catch-22", MatchKind.NearTitle, ["Joseph Heller"], null, 300, ["catch"]),
        ["rowling_e"] = F("OL82563W", "Harry Potter and the Philosopher's Stone", MatchKind.Character, ["J. K. Rowling"], 1997, 400) with { MatchedCharacters = ["Hermione Granger"] },
        ["hd_c"] = F("OL12W", "HERmione", MatchKind.NearTitle, ["H.D."], 1981, 10, ["hermione"]),
        ["austen_e"] = F("OL13W", "Pride and Prejudice", MatchKind.Character, ["Jane Austen"], 1813, 4042) with { MatchedCharacters = ["Elizabeth Bennet"] },
        ["salinger_e"] = F("OL14W", "The Catcher in the Rye", MatchKind.Character, ["J. D. Salinger"], 1951, 500, [], [("Shao jia yun", "translator")])
            with { MatchedCharacters = ["Holden Caulfield"] },
        ["jekyll_a"] = F("OL15W", "Dr. Jekyll and Mr. Hyde", MatchKind.ExactTitle, ["Robert Louis Stevenson"], 1886, 800, ["jekyll", "hyde"]),
        ["three_body_a"] = F("OL17267881W", "三体", MatchKind.ExactTitle, ["刘慈欣"], 2006, 45)
            with { AlsoKnownAs = "The Three-Body Problem", AlsoKnownAsLabel = "known in English as" },
        ["pooh_topic"] = F("OL262758W", "Winnie-the-Pooh", MatchKind.Topic, ["A. A. Milne"], null, 87) with { MatchedSubjects = ["Bears", "Teddy bears"] },
        ["pooh_suggested"] = F("OL262758W", "Winnie-the-Pooh", MatchKind.Suggested, ["A. A. Milne"], null, 87),
        ["crafted_subject"] = F("OL9W", "Little Bear", MatchKind.Topic, ["Else Holmelund Minarik"], null, 40) with { MatchedSubjects = ["Bears free download at evil"] },
        ["bilbo_c"] = F("OL20651930W", "Bilbo", MatchKind.NearTitle, ["Charles Dixon"], null, 2, ["hobbit"])
            with { AlsoKnownAs = "Bilbo le Hobbit", AlsoKnownAsLabel = "also published as" },
        ["king_d"] = F("OL81634W", "The Long Walk", MatchKind.AuthorOnly, ["Stephen King"], 1979, 60) with { AuthorAlias = new AuthorAliasFact("Richard Bachman", "Stephen King") },
        ["fisher_e"] = F("OL20W", "The Hobbit", MatchKind.TitleOnly, ["Jude Fisher"], 2012, 3, ["hobbit"]),
        ["weak_e"] = F("OL21W", "Collected Letters", MatchKind.Weak, ["Jane Doe"], null, 2),
        ["huck_partial_e"] = F("OL53908W", "Adventures of Huckleberry Finn", MatchKind.PartialTitle, ["Mark Twain"], 1876, 2623, ["huckleberry"]),
        ["blake_d"] = F("OL45846W", "Matilda", MatchKind.ContributorOnly, ["Roald Dahl"], 1988, 300, [], [("Quentin Blake", "illustrator")], matchedContributor: "Quentin Blake"),
        ["pp_inferred_a"] = F("OL66554W", "Pride and Prejudice", MatchKind.InferredTitle, ["Jane Austen"], 1813, 4042) with { MatchedCharacters = ["Elizabeth Bennet"] },
        ["authorless_a"] = F("OL30W", "Beowulf", MatchKind.ExactTitle, [], null, 900),
    };

    public static ExplanationFacts F(
        string id, string title, MatchKind kind, string[] primaries, int? year, int editions,
        string[]? terms = null, (string Name, string Role)[]? contributors = null, string? matchedContributor = null) => new()
    {
        Id = id,
        Title = title,
        Kind = kind,
        MatchPhrase = ExplanationFactsBuilder.PhraseFor(kind),
        PrimaryAuthors = primaries,
        Contributors = (contributors ?? []).Select(c => new ContributorFact(c.Name, c.Role)).ToList(),
        MatchedContributor = matchedContributor,
        FirstPublishYear = year,
        EditionCount = editions,
        MatchedTerms = terms ?? [],
    };

    /// <summary>Every field as text, to compare facts by value (records compare lists by reference).</summary>
    public static string Describe(ExplanationFacts f) => string.Join(" | ",
        f.Id, f.Title, f.MatchPhrase, f.Kind, string.Join(",", f.PrimaryAuthors), string.Join(",", f.Contributors.Select(c => $"{c.Name}:{c.Role}")),
        f.MatchedContributor, f.FirstPublishYear, f.EditionCount, string.Join(",", f.MatchedTerms), string.Join(",", f.MatchedCharacters),
        f.AlsoKnownAs, f.AlsoKnownAsLabel, f.AuthorAlias, f.Intent, f.DuplicateRecords, string.Join(",", f.MatchedSubjects));
}
