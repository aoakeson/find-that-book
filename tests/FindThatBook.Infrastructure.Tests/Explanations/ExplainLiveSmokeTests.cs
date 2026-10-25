using System.Text.Json;
using FindThatBook.Core.Explanations;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Explanations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Infrastructure.Tests.Explanations;

/// <summary>
/// Opt-in live explain calls through the real writer and StructuredLlm, reporting the grounding pass rate.
/// Never runs in CI: explicit, and skipped unless FTB_LIVE_EXPLAIN=1. One call per batch; FTB_SMOKE_BATCHES caps the
/// calls (default 1). Keys come from the environment and are never printed.
/// Run: <c>set -a; source .env; set +a; FTB_LIVE_EXPLAIN=1 FTB_SMOKE_PROVIDER=gemini FTB_SMOKE_BATCHES=4
/// FTB_SMOKE_OUT=/tmp/explain.jsonl dotnet test --project tests/FindThatBook.Infrastructure.Tests -- --explicit only
/// --filter-class *ExplainLiveSmokeTests</c>. FTB_SMOKE_REPLAY=&lt;a previous FTB_SMOKE_OUT file&gt; re-grounds the recorded
/// sentences with the current check and makes no call.
/// </summary>
public sealed class ExplainLiveSmokeTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task Explain_through_the_real_writer()
    {
        if (Environment.GetEnvironmentVariable("FTB_LIVE_EXPLAIN") != "1") Assert.Skip("FTB_LIVE_EXPLAIN != 1");
        var provider = Environment.GetEnvironmentVariable("FTB_SMOKE_PROVIDER") ?? "gemini";
        var batches = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_BATCHES"), out var n) ? n : 1;
        var skip = int.TryParse(Environment.GetEnvironmentVariable("FTB_SMOKE_SKIP"), out var s) ? s : 0;
        var (section, envKey) = provider switch
        {
            "gemini" => ("Gemini", "GEMINI_API_KEY"),
            "anthropic" => ("Anthropic", "ANTHROPIC_API_KEY"),
            "openai" => ("OpenAI", "OPENAI_API_KEY"),
            _ => throw new ArgumentException(provider),
        };
        var settings = new Dictionary<string, string?>
        {
            ["AI:Provider"] = provider,
            [$"AI:{section}:ApiKey"] = Environment.GetEnvironmentVariable(envKey),
            ["AI:Cache:Enabled"] = "false",
            [$"AI:{section}:RequestsPerMinute"] = "0",   // the smoke counts its own calls; no local rate gate
        };
        if (Environment.GetEnvironmentVariable("FTB_SMOKE_MODEL") is { Length: > 0 } model) settings[$"AI:{section}:Model"] = model;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton(new FindThatBook.Core.Search.SearchOptions());
        services.AddAi(configuration);
        services.AddExplanationWriter(configuration);
        await using var sp = services.BuildServiceProvider();
        var writer = sp.GetRequiredService<IExplanationWriter>();
        var replay = Environment.GetEnvironmentVariable("FTB_SMOKE_REPLAY") is { Length: > 0 } replayPath
            ? File.ReadAllLines(replayPath).Select(l => JsonDocument.Parse(l).RootElement).ToList()
            : null;

        var lines = new List<string>();
        foreach (var (name, query, works, intent) in Batches().Skip(skip).Take(batches))
        {
            var cleaned = LlmExplanationWriterTests.Query(query) with
            {
                Intents = intent is { } i ? new HashSet<EditionIntent> { i } : new HashSet<EditionIntent>(),
                Topic = name == "topic" ? ["bears"] : [],
            };
            var facts = ExplanationFactsBuilder.Build(works, cleaned, null);
            var result = replay is null
                ? await writer.WriteAsync(facts, TestContext.Current.CancellationToken)
                : Replayed(replay, provider, name);
            foreach (var f in facts)
            {
                var sentence = result.Value?.GetValueOrDefault(f.Id);
                var check = sentence is null ? null : GroundingCheck.Check(sentence, f);
                var line = JsonSerializer.Serialize(new
                {
                    provider,
                    model = result.Call.Model,
                    batch = name,
                    status = result.Call.Status.ToString(),
                    detail = result.Call.Detail,
                    ms = result.Call.ElapsedMs,
                    input_tokens = result.Call.InputTokens,
                    output_tokens = result.Call.OutputTokens,
                    id = f.Id,
                    kind = f.Kind.ToString(),
                    sentence,
                    passed = check?.Passed,
                    problems = check?.Problems,
                    template = TemplateExplainer.Explain(f),
                });
                output.WriteLine(line);
                lines.Add(line);
            }
        }
        if (Environment.GetEnvironmentVariable("FTB_SMOKE_OUT") is { Length: > 0 } path) await File.AppendAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
    }

    private static LlmResult<IReadOnlyDictionary<string, string>> Replayed(List<JsonElement> rows, string provider, string batch)
    {
        var mine = rows.Where(r => r.GetProperty("provider").GetString() == provider && r.GetProperty("batch").GetString() == batch).ToList();
        var sentences = mine.Where(r => r.GetProperty("sentence").ValueKind == JsonValueKind.String)
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("sentence").GetString()!);
        var model = mine.FirstOrDefault() is { ValueKind: JsonValueKind.Object } first ? first.GetProperty("model").GetString() ?? "" : "";
        return new(sentences, new LlmCall("explain", "replay", model, LlmExplanationWriter.PromptId, LlmCallStatus.Ok, true, null, null, 0, "replay"));
    }

    /// <summary>Realistic ranker output for golden-style queries (ids and data from recorded Open Library responses).</summary>
    private static IEnumerable<(string Name, string Query, RankedWork[] Works, EditionIntent? Intent)> Batches()
    {
        yield return ("tolkien", "tolkien hobbit illustrated deluxe 1937",
        [
            W("OL27482W", "The Hobbit", MatchTier.A, ["J.R.R. Tolkien"], [("Alan Lee", "Illustrator")], 1937, 482,
                new MatchEvidence { ExactTitle = true, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien", YearMatched = true }),
            W("OL219602W", "The Hobbit", MatchTier.A, ["J.R.R. Tolkien"], [("Charles Dixon", "Adapter"), ("Sean Deming", "Adapter")], 1990, 10,
                new MatchEvidence { ExactTitle = true, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" }),
            W("OL1W", "The Annotated Hobbit", MatchTier.C, ["J.R.R. Tolkien"], [("Douglas A. Anderson", "Editor")], 1988, 30,
                new MatchEvidence { TitleScore = 0.7, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" }),
            W("OL27513W", "The Fellowship of the Ring", MatchTier.D, ["J.R.R. Tolkien"], [], 1954, 300,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" }),
            W("OL27479W", "The Two Towers", MatchTier.D, ["J.R.R. Tolkien"], [], 1954, 250,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "J.R.R. Tolkien" }),
        ], null);
        yield return ("contributor", "hobbit dixon graphic novel",
        [
            W("OL219602W", "The Hobbit", MatchTier.B, ["J.R.R. Tolkien"], [("Charles Dixon", "Adapter"), ("Sean Deming", "Adapter")], 1990, 10,
                new MatchEvidence { ExactTitle = true, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Contributor, MatchedAuthor = "Charles Dixon", MatchedContributorRole = "Adapter", IntentMatch = true }),
            W("OL27482W", "The Hobbit", MatchTier.E, ["J.R.R. Tolkien"], [("Alan Lee", "Illustrator")], 1937, 482,
                new MatchEvidence { ExactTitle = true, TitleScore = 1, MatchedTitleTerms = ["hobbit"], ForeignAuthor = true }),
            W("OL45846W", "Matilda", MatchTier.D, ["Roald Dahl"], [("Quentin Blake", "Illustrator")], 1988, 300,
                new MatchEvidence { Role = AuthorRole.Contributor, MatchedAuthor = "Quentin Blake", MatchedContributorRole = "Illustrator" }),
            W("OL118421W", "A Tale of Two Cities", MatchTier.D, ["Charles Dickens"], [], 1800, 3196,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Charles Dickens" }),
        ], EditionIntent.Adaptation);
        yield return ("characters", "austen bennet",
        [
            W("OL66554W", "Pride and Prejudice", MatchTier.A, ["Jane Austen"], [], 1813, 4042,
                new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.Hypothesis, Role = AuthorRole.Primary, MatchedAuthor = "Jane Austen", MatchedCharacters = ["bennet"] },
                persons: ["Elizabeth Bennet", "Fitzwilliam Darcy"]),
            W("OL82563W", "Harry Potter and the Philosopher's Stone", MatchTier.E, ["J. K. Rowling"], [], 1997, 400,
                new MatchEvidence { CharacterCoverage = 1, MatchedCharacters = ["hermione"] }, persons: ["Hermione Granger", "Harry Potter"]),
            W("OL12W", "HERmione", MatchTier.A, ["H.D."], [], 1981, 10, new MatchEvidence { ExactTitle = true, MatchedTitleTerms = ["hermione"] }),
            W("OL53908W", "Adventures of Huckleberry Finn", MatchTier.C, ["Mark Twain"], [], 1876, 2623,
                new MatchEvidence { TitleScore = 0.5, MatchedTitleTerms = ["huckleberry"], Role = AuthorRole.Primary, MatchedAuthor = "Mark Twain", AuthorFromHypothesis = true }),
            W("OL5W", "Complete Poems, 1904-1962", MatchTier.D, ["E. E. Cummings"], [], 1991, 20, new MatchEvidence { Role = AuthorRole.Primary }),
        ], null);
        yield return ("scripts", "the three body problem",
        [
            W("OL17267881W", "三体", MatchTier.A, ["刘慈欣"], [], 2006, 45,
                new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "The Three-Body Problem", MatchedTitleTerms = ["three", "body", "problem"] }),
            W("OL81634W", "The Long Walk", MatchTier.D, ["Stephen King"], [], 1979, 60,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Stephen King", AuthorViaAlternativeName = true },
                alternativeNames: ["Richard Bachman"]),
            W("OL166894W", "Преступление и наказание", MatchTier.A, ["Фёдор Михайлович Достоевский"], [("Constance Garnett", "Translator")], 1866, 1100,
                new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "Crime and Punishment", Role = AuthorRole.Primary }),
            W("OL20W", "Collected Letters", MatchTier.E, ["Jane Doe"], [], null, 2, new MatchEvidence()),
        ], null);
        // Problems seen in live end-to-end responses: contributor noise, a junk "Author" credit, garbled edition-title labels.
        yield return ("e2e", "hobbit dixon",
        [
            W("OL219602W", "The Hobbit", MatchTier.B, ["J.R.R. Tolkien"], [("Charles Dixon", "Adapter"), ("Sean Deming", "Adapter"), ("Donato Giancola", "Illustrator")], 1990, 10,
                new MatchEvidence { ExactTitle = true, MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Contributor, MatchedAuthor = "Charles Dixon", MatchedContributorRole = "Adapter" }),
            W("OL27482W", "The Hobbit", MatchTier.E, ["J.R.R. Tolkien"], [("Michael Hague", "Illustrator"), ("Author", "Illustrator"), ("Alan Lee", "Illustrator")], 1937, 482,
                new MatchEvidence { ExactTitle = true, TitleScore = 1, MatchedTitleTerms = ["hobbit"], ForeignAuthor = true }),
            W("OL20651930W", "Bilbo", MatchTier.C, ["Charles Dixon"], [], null, 2,
                new MatchEvidence { TitleScore = 0.5, TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "Bilbo le Hobbit", MatchedTitleTerms = ["hobbit"], Role = AuthorRole.Primary, MatchedAuthor = "Charles Dixon" }),
            W("OL413674W", "Tom Sawyer Abroad", MatchTier.D, ["Mark Twain"], [("Erik Sellin", "Narrator"), ("Nat Hentoff", "Introduction"), ("Dan Beard", "Illustrator")], 1894, 333,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Mark Twain", TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "Tom Sawyer ilmailija Huckleberry Finn'in jatko" }),
            W("OL69630W", "A Little Princess", MatchTier.E, ["Frances Hodgson Burnett"], [("Teresa Gallagher", "Narrator"), ("Graham Rust", "Illustrator"), ("Thea Kliros", "Illustrator")], 1905, 600,
                new MatchEvidence { TitleScore = 0.4, MatchedTitleTerms = ["little"] }),
            W("OL17267881W", "三体", MatchTier.A, ["刘慈欣"], [("Vincent Schmitt", "also credited")], 2006, 45,
                new MatchEvidence { ExactTitle = true, TitleSource = TitleSource.EditionTitle, MatchedEditionTitle = "The three-body problem" }),
        ], null);
        // Topic request: subject evidence vs a verified suggestion without it.
        yield return ("topic", "find me a book about bears",
        [
            W("OL262758W", "Winnie-the-Pooh", MatchTier.E, ["A. A. Milne"], [("Ernest H. Shepard", "Illustrator")], 1926, 87,
                new MatchEvidence { MatchedSubjects = ["Bears -- Juvenile fiction", "Teddy bears"], SuggestedByInterpreter = true }),
            W("OL1W", "Little Bear", MatchTier.E, ["Else Holmelund Minarik"], [("Maurice Sendak", "Illustrator")], 1957, 40,
                new MatchEvidence { MatchedSubjects = ["Bears"] }),
            W("OL2W", "A Bear Called Paddington", MatchTier.E, ["Michael Bond"], [], 1958, 120,
                new MatchEvidence { SuggestedByInterpreter = true }),
            W("OL3W", "We're Going on a Bear Hunt", MatchTier.E, ["Michael Rosen"], [("Helen Oxenbury", "Illustrator")], 1989, 60,
                new MatchEvidence { MatchedSubjects = ["Bears", "Hunting -- Fiction"] }),
        ], null);
        // AI-34: limit=10 with long titles and two contributors each (455 of 600 tokens were used here).
        yield return ("ten", "dickens",
        [
            .. Enumerable.Range(1, 10).Select(i => W($"OL{900 + i}W",
                $"Collected Works Volume {i}: Being a Very Long Subtitle About the Complete Correspondence", MatchTier.D,
                ["Charles Dickens", "Wilkie Collins"], [("Some Editor", "Editor"), ("Another Person", "Illustrator")], null, 100 + i,
                new MatchEvidence { Role = AuthorRole.Primary, MatchedAuthor = "Charles Dickens" })),
        ], null);
    }

    private static RankedWork W(string id, string title, MatchTier tier, string[] primaries, (string Name, string Role)[] contributors, int? year, int editions,
        MatchEvidence evidence, string[]? persons = null, string[]? alternativeNames = null) => new()
    {
        Work = new CandidateWork
        {
            WorkId = id, Title = title, AuthorNames = primaries, FirstPublishYear = year, EditionCount = editions,
            Persons = persons ?? [], AuthorAlternativeNames = alternativeNames ?? [],
        },
        Tier = tier,
        Score = 1,
        PrimaryAuthors = [.. primaries.Select(p => new AuthorCredit(p, null))],
        Contributors = [.. contributors.Select(c => new AuthorCredit(c.Name, null, c.Role))],
        Evidence = evidence,
    };
}
