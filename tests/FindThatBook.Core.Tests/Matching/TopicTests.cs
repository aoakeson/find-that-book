using FindThatBook.Core.Explanations;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;
using FindThatBook.Core.Search;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// Topic requests ("find me a book about bears") on live Open Library pools recorded for this feature: the subject
/// search and suggestion calls, subject evidence, topic order, status and hint. Queries go through the real cleaner,
/// checked against the cleaner output the feature specifies for each recorded query.
/// </summary>
public sealed class TopicTests
{
    /// <summary>(fixture, expected #1, works whose subjects or suggestion must back every one of the top 5).</summary>
    public static TheoryData<string, string> TopicPools => new()
    {
        { "TOPIC:bears:off", "OL832133W" },              // We're Going on a Bear Hunt
        { "TOPIC:bears:llm", "OL476641W" },              // Winnie-the-Pooh (suggested, most editions)
        { "TOPIC:dragons:off", "OL99529W" },             // The Book of Dragons
        { "TOPIC:paris:off", "OL41072W" },               // The Murders in the Rue Morgue
        { "TOPIC:french-revolution:off", "OL8193465W" }, // A Tale of Two Cities
        { "TOPIC:cats:off", "OL1898309W" },              // The Cat in the Hat
        { "TOPIC:cats-lessing:off", "OL257820W" },       // On Cats (Doris Lessing)
    };

    [Theory]
    [MemberData(nameof(TopicPools))]
    public void A_topic_request_lists_books_about_the_topic(string id, string top)
    {
        var r = Run(id);
        r.Top.Work.WorkId.ShouldBe(top, $"top 3: {string.Join(",", r.TopIds(3))}");
        r.Ranking.Ranked.Take(5).ShouldAllBe(w => w.Evidence.MatchedSubjects.Count > 0 || w.Evidence.SuggestedByInterpreter);
        r.Ranking.Status.ShouldBe(MatchStatus.Ambiguous);
        r.Ranking.Reason.ShouldBe(StatusReason.TopicSearch);
        StatusPolicy.ShouldJudge(r.Ranking).ShouldBeFalse(); // there is no one right book for the judge to confirm
        StatusPolicy.Decide(r.Ranking, JudgeVerdict.Off, 0.9).Hint.ShouldBe(StatusPolicy.TopicHint); // the page heads the list with the topic
        r.Steps.ShouldNotContain(CatalogSteps.PersonFallback);
        r.Steps.ShouldNotContain(CatalogSteps.AnyOf);
    }

    [Theory]
    [MemberData(nameof(AllTopicCases))]
    public void The_cleaner_reads_each_recorded_query_as_the_feature_specifies(string id)
    {
        var c = PoolFixtures.Case(id);
        var spec = c.Cleaned!;
        var q = QueryCleaner.Clean(c.Query);
        q.Search.ShouldBe(spec.Search);
        q.Topic.ShouldBe(spec.Topic);
        q.TopicAuthor.ShouldBe(spec.TopicAuthor);
        spec.Request.ShouldAllBe(t => q.RequestTokens.Contains(t));
    }

    public static TheoryData<string> AllTopicCases => PoolFixtures.Ids(PoolFixtures.Topic);

    [Fact]
    public void Without_the_topic_rules_the_same_pool_is_a_title_search() // what "find me a book about bears" did before
    {
        var c = PoolFixtures.Case("TOPIC:bears:off");
        var before = Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default with { Topics = false });
        before.Top.Work.Title.ShouldBe("Bear"); // a novel titled "Bear", by title words alone
        before.Ranking.Status.ShouldBe(MatchStatus.Match);
    }

    [Fact]
    public void The_subject_search_uses_the_first_topic_and_the_author_hint()
    {
        var bears = Run("TOPIC:bears:llm");
        bears.Issued.Single(i => i.Step == CatalogSteps.Topic).Query.Q.ShouldBe("subject:(bears)"); // not (bears AND animals)
        bears.Issued.Single(i => i.Step == CatalogSteps.Topic).Query.IncludeSubjects.ShouldBeTrue();
        var suggestions = bears.Issued.Single(i => i.Step == CatalogSteps.Suggestions).Query;
        suggestions.Q.ShouldBe("title:(winnie pooh) OR title:(bear called paddington) OR title:(corduroy) OR title:(were going bear hunt) OR title:(little bear)");
        bears.Ranking.Ranked.Take(5).Count(w => w.Evidence.SuggestedByInterpreter).ShouldBe(5);

        var lessing = Run("TOPIC:cats-lessing:off");
        var topic = lessing.Issued.Single(i => i.Step == CatalogSteps.Topic).Query;
        topic.Q.ShouldBe("subject:(cats)");
        topic.Author.ShouldBe("doris lessing");
        lessing.Ranking.Ranked.Take(5).ShouldAllBe(w => w.PrimaryAuthors.Any(a => a.Name == "Doris Lessing"));
    }

    [Fact]
    public void An_unknown_topic_is_none_and_an_empty_topic_asks_for_one()
    {
        var unknown = Run("TOPIC:xqzv:off");
        unknown.Steps.ShouldBe([CatalogSteps.Broad, CatalogSteps.Topic]); // one word: no OR retry
        unknown.Ranking.TopicLabel.ShouldBe("xqzv");
        var none = StatusPolicy.Decide(unknown.Ranking, JudgeVerdict.Off, 0.9);
        none.Status.ShouldBe(MatchStatus.None);
        none.Reason.ShouldBe(StatusReason.NoCandidates); // "none" means no candidates, topic or not
        none.Hint.ShouldBe("Try a broader topic, or add a title or author."); // the page heads it "No books found about xqzv"

        var nothingRetrieved = MatchRanker.Rank(QueryCleaner.Clean("a book about xqzv"), null, []);
        nothingRetrieved.TopicLabel.ShouldBe("xqzv");
        StatusPolicy.Decide(nothingRetrieved, JudgeVerdict.Off, 0.9).ShouldBe(none);

        var empty = Run("TOPIC:empty:llm");
        empty.Steps.ShouldBe([CatalogSteps.Broad]);
        var decision = StatusPolicy.Decide(empty.Ranking, JudgeVerdict.Off, 0.9);
        decision.Status.ShouldBe(MatchStatus.None);
        decision.Reason.ShouldBe(StatusReason.NoCandidates);
        decision.Hint.ShouldBe("Add a topic, a title or an author.");
    }

    /// <summary>
    /// The real pipeline (SearchService, LLM off) over the recorded live responses, so the replay above cannot drift
    /// from what the service merges and reports. The catalog answers only the exact recorded queries.
    /// </summary>
    [Fact]
    public async Task The_pipeline_with_the_llm_off_answers_the_recorded_live_queries()
    {
        var lessing = await Search("TOPIC:cats-lessing:off");
        lessing.Status.ShouldBe(MatchStatus.Ambiguous);
        lessing.Reason.ShouldBe(StatusReason.TopicSearch);
        lessing.Hint.ShouldBe(StatusPolicy.TopicHint);
        lessing.Topic.ShouldNotBeNull().Terms.ShouldBe(["cats"]);
        lessing.Items.Take(2).Select(i => i.Work.Work.Title).ShouldBe(["On Cats", "Particularly cats"]);
        lessing.Items.ShouldAllBe(i => i.Work.Evidence.MatchedSubjects.Count > 0);
        lessing.Trace.CatalogCalls.Select(c => c.Step).ShouldBe([CatalogSteps.Broad, CatalogSteps.Topic]);

        lessing.Items.ShouldAllBe(i => i.Work.PrimaryAuthors.Any(a => a.Name == "Doris Lessing"));

        foreach (var id in new[] { "TOPIC:yellow-paper:off", "TOPIC:yellow-paper-smith:off" })
        {
            var yellow = await Search(id);
            yellow.Topic.ShouldBeNull(id); // the typed text is a book's title
            yellow.Items[0].Work.Work.Title.ShouldBe("Novel on yellow paper", id);
            yellow.Items[0].Work.PrimaryAuthors.ShouldContain(a => a.Name == "Stevie Smith", id);
        }
        (await Search("TOPIC:yellow-paper-smith:off")).Status.ShouldBe(MatchStatus.Match); // the typed author settles it

        var unknown = await Search("TOPIC:xqzv:off");
        unknown.Status.ShouldBe(MatchStatus.None);
        unknown.Reason.ShouldBe(StatusReason.NoCandidates);
        unknown.Hint.ShouldBe(StatusPolicy.TopicNoneHint);
        unknown.Items.ShouldBeEmpty();
        unknown.Topic.ShouldNotBeNull();
    }

    private static async Task<SearchOutcome> Search(string id)
    {
        var c = PoolFixtures.Case(id);
        var catalog = new RecordedCatalog(c);
        var service = new SearchService(new InterpretationService(new NoInterpreter()), catalog,
            new ExplanationService(new NoWriter()), new NoJudge(), new SearchOptions(), TimeProvider.System);
        var outcome = await service.SearchAsync(c.Query, null, TestContext.Current.CancellationToken);
        catalog.Unrecorded.ShouldBeEmpty(); // every call the service made is one Open Library answered live
        return outcome;
    }

    private sealed class RecordedCatalog(PoolCase c) : IBookCatalog
    {
        private readonly Lock _gate = new();
        public List<string> Unrecorded { get; } = [];

        public Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
        {
            var call = c.Calls.FirstOrDefault(r => r.Params.GetValueOrDefault("q") == query.Q
                                                   && r.Params.GetValueOrDefault("title") == query.Title
                                                   && r.Params.GetValueOrDefault("author") == query.Author);
            if (call is null) lock (_gate) Unrecorded.Add(query.ToString());
            IReadOnlyList<CandidateWork> works = call?.Works ?? [];
            return Task.FromResult(new CatalogResponse(works, works.Count, new CatalogCallStats(false, 1, 0)));
        }
    }

    private sealed class NoInterpreter : IQueryInterpreter
    {
        public Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct)
            => Task.FromResult(new LlmResult<QueryHypothesis>(null,
                new LlmCall("interpret", "none", "", "interpret.v4", LlmCallStatus.NotConfigured, false, null, null, 0)));
    }

    private sealed class NoWriter : IExplanationWriter
    {
        public Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct)
            => Task.FromResult(new LlmResult<IReadOnlyDictionary<string, string>>(null,
                new LlmCall("explain", "none", "", "explain.v4", LlmCallStatus.NotConfigured, false, null, null, 0)));
    }

    private sealed class NoJudge : IMatchJudge
    {
        public bool IsEnabled => false;
        public Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct) => Task.FromResult(JudgeVerdict.Off);
    }

    [Fact]
    public void A_subject_search_with_no_hits_retries_with_any_one_topic()
    {
        var query = Query("books about pirates and treasure", search: ["pirates", "treasure"], topic: ["pirates", "treasure"]);
        var hypothesis = new QueryHypothesis(null, null, [], [], null, null) { Intent = QueryIntent.Topic, Subjects = ["pirates", "buried treasure"] };
        var first = RetrievalLadder.AfterInterpretation(query, hypothesis);
        first.Single().Query.Q.ShouldBe("subject:(pirates)");
        var steps = new Dictionary<string, int?> { [CatalogSteps.Broad] = 20, [CatalogSteps.Topic] = 0 };
        var next = RetrievalLadder.Next(new LadderState(query, hypothesis, [], RankingResult.Empty, steps, 3));
        next!.Step.ShouldBe(CatalogSteps.TopicAny);
        next.Query.Q.ShouldBe("subject:(pirates) OR subject:(buried AND treasure)");
        steps[CatalogSteps.Topic] = 12;
        RetrievalLadder.Next(new LadderState(query, hypothesis, [], RankingResult.Empty, steps, 3)).ShouldBeNull(); // no fallbacks in topic mode
    }

    [Fact]
    public void A_typed_text_that_is_a_title_stays_a_book_search()
    {
        var query = Query("a book about bears", search: ["bears"], topic: ["bears"]);
        var pool = new[]
        {
            MatchRankerTests.Work("OL1W", "A Book About Bears", ["Ann Writer"], 30) with { Subjects = ["Bears"] },
            MatchRankerTests.Work("OL2W", "Little Bear", ["Else Holmelund Minarik"], 69) with { Subjects = ["Bears"] },
        };
        var r = MatchRanker.Rank(query, null, pool);
        r.TopicLabel.ShouldBeNull();
        r.Reason.ShouldNotBe(StatusReason.TopicSearch);
        r.Ranked[0].Work.WorkId.ShouldBe("OL1W");
        r.Ranked[0].Tier.ShouldBe(MatchTier.A);
    }

    [Fact]
    public void A_real_title_the_cleaner_reads_as_a_topic_stays_a_book_search() // Stevie Smith, LLM off
    {
        var c = PoolFixtures.Case("TOPIC:yellow-paper:off");
        TopicRequest.From(QueryCleaner.Clean(c.Query), null).ShouldNotBeNull(); // the frame rule alone cannot tell this title from a topic
        var r = Run(c.Id);
        r.Ranking.TopicLabel.ShouldBeNull();
        r.Ranking.Reason.ShouldNotBe(StatusReason.TopicSearch);
        r.Top.Work.Title.ShouldBe("Novel on yellow paper");
        r.Top.Tier.ShouldBe(MatchTier.A);
        r.Top.PrimaryAuthors.ShouldContain(a => a.Name == "Stevie Smith");
        // As in any book search, a famous partial title (The Yellow Wallpaper, 721 editions) outscoring an obscure exact one
        // keeps the status ambiguous; the list still leads with the title the user typed.
        r.Ranking.Reason.ShouldBe(StatusReason.ScoreConflict);
        r.Ranking.Ranked.Select(w => w.Work.Title).ShouldContain("The Yellow Wallpaper");
    }

    [Fact]
    public void A_one_word_topic_is_not_searched_twice()
    {
        var query = QueryCleaner.Clean("books about dragons");
        var steps = new Dictionary<string, int?> { [CatalogSteps.Broad] = 20, [CatalogSteps.Topic] = 0 };
        RetrievalLadder.Next(new LadderState(query, null, [], RankingResult.Empty, steps, 3)).ShouldBeNull();
        var revolution = QueryCleaner.Clean("books about the french revolution");
        RetrievalLadder.AfterInterpretation(revolution, null).Single().Query.Q.ShouldBe("subject:(french AND revolution)");
        RetrievalLadder.Next(new LadderState(revolution, null, [], RankingResult.Empty, steps, 3))!.Query.Q
            .ShouldBe("subject:(french) OR subject:(revolution)");
    }

    [Fact]
    public void Request_words_count_as_matched_when_a_title_holds_them() // "find me aciman" → Find Me
    {
        var query = new CleanedQuery
        {
            Raw = "find me aciman", Text = "find me aciman", All = ["find", "me", "aciman"], Search = ["aciman"],
            Optional = new HashSet<string>(), Head = [], SoftTokens = new HashSet<string>(), Intents = new HashSet<EditionIntent>(),
            RemovedNoise = [], RequestTokens = new HashSet<string> { "find", "me" },
        };
        var pool = new[] { MatchRankerTests.Work("OL1W", "Find Me", ["André Aciman"], 30), MatchRankerTests.Work("OL2W", "Enigma Variations", ["André Aciman"], 20) };
        var r = MatchRanker.Rank(query, null, pool);
        r.Ranked[0].Work.WorkId.ShouldBe("OL1W");
        r.Ranked[0].Tier.ShouldBe(MatchTier.A);
        r.Status.ShouldBe(MatchStatus.Match);
    }

    [Fact]
    public void A_phrase_needs_its_words_in_one_heading_and_whole_words_only()
    {
        var query = Query("books about the french revolution", search: ["french", "revolution"], topic: ["french revolution"]);
        var pool = new[]
        {
            MatchRankerTests.Work("OL1W", "Citizens", ["Simon Schama"], 20) with { Subjects = ["France -- History -- Revolution", "French Revolution"] },
            MatchRankerTests.Work("OL2W", "The Rebel", ["Albert Camus"], 80) with { Subjects = ["Revolutions"] },
            MatchRankerTests.Work("OL3W", "Bearings", ["Some Engineer"], 90) with { Subjects = ["Bearings (Machinery)"] },
            MatchRankerTests.Work("OL4W", "Essays", ["Anne Author"], 60) with { Subjects = ["French literature", "Revolutions"] },
        };
        var r = MatchRanker.Rank(query, null, pool);
        r.Ranked.Select(w => w.Work.WorkId).ShouldBe(["OL1W"]); // only evidence-backed works are listed
        r.Ranked[0].Evidence.MatchedSubjects.ShouldBe(["French Revolution"]);
    }

    [Fact]
    public void Typed_phrases_are_each_wanted_and_different_headings_may_cover_them() // "books about bears and wolves"
    {
        var c = PoolFixtures.Case("TOPIC:bears-wolves:off");
        var r = Run(c.Id);
        r.Issued.Single(i => i.Step == CatalogSteps.Topic).Query.Q.ShouldBe("subject:(bears) OR subject:(wolves)");
        r.Top.Work.Title.ShouldBe("The True Story of the Three Little Pigs"); // "Bears, fiction" and "By A. Wolf" / "Wolves"
        r.Top.Evidence.MatchedSubjects.ShouldContain("By A. Wolf");
        r.Top.Evidence.MatchedSubjects.ShouldContain("Bears, fiction");
        r.Ranking.Ranked.ShouldAllBe(w => w.Evidence.MatchedSubjects.Count > 0);

        var query = Query("books about bears and wolves", search: ["bears", "wolves"], topic: ["bears", "wolves"]);
        var both = MatchRanker.Rank(query, null,
        [
            MatchRankerTests.Work("OL1W", "Bear Stories", ["Ann Writer"], 30) with { Subjects = ["Bears"] },
            MatchRankerTests.Work("OL2W", "Into the Woods", ["Bo Writer"], 30) with { Subjects = ["Bears", "Wolves"] },
        ]);
        both.Ranked[0].Work.WorkId.ShouldBe("OL2W");
    }

    [Theory]
    [InlineData("foxes", "Fox hunting", true)]
    [InlineData("fox", "Foxes, fiction", true)]
    [InlineData("wolves", "By A. Wolf", true)]
    [InlineData("wolf", "Wolves", true)]
    [InlineData("stories", "Story telling", true)]
    [InlineData("story", "Short stories", true)]
    [InlineData("bears", "Bear hunting", true)]
    [InlineData("bear", "Bears", true)]
    [InlineData("knives", "Knife throwing", true)]
    [InlineData("churches", "Church history", true)]
    [InlineData("news", "New York (N.Y.)", false)]
    [InlineData("paris", "Pari-mutuel betting", false)]
    [InlineData("glass", "Glas", false)]
    [InlineData("series", "Seri Indians", false)]
    [InlineData("bear", "Bearings (Machinery)", false)]
    public void Subject_words_match_whole_words_singular_or_plural(string word, string heading, bool expected)
        => TopicTerms.InHeading(word, TopicTerms.HeadingForms(heading)).ShouldBe(expected);

    [Fact]
    public void A_larger_duplicate_without_subjects_does_not_replace_the_record_that_has_them()
    {
        var query = Query("books about bears", search: ["bears"], topic: ["bears"]);
        var r = MatchRanker.Rank(query, null,
        [
            MatchRankerTests.Work("OL1W", "Little Bear", ["Else Holmelund Minarik"], 5) with { Subjects = ["Bears"] },
            MatchRankerTests.Work("OL2W", "Little Bear", ["Else Holmelund Minarik"], 60),
            MatchRankerTests.Work("OL3W", "Corduroy", ["Don Freeman"], 57) with { Subjects = ["Teddy bears"] },
        ]);
        var little = r.Ranked.Single(w => w.Work.Title == "Little Bear");
        little.Work.WorkId.ShouldBe("OL1W");
        little.FoldedWorkIds.ShouldContain("OL2W");
        little.Evidence.MatchedSubjects.ShouldBe(["Bears"]);
        r.Ranked.Count.ShouldBe(2);
    }

    [Fact]
    public void A_named_author_limits_every_branch_and_another_named_author_is_penalised()
    {
        var lessing = Run("TOPIC:cats-lessing:off"); // the broad call also found Lessing works with no cat subjects
        lessing.Ranking.Ranked.ShouldAllBe(w => w.PrimaryAuthors.Any(a => a.Name == "Doris Lessing") && w.Evidence.MatchedSubjects.Count > 0);

        var hypothesis = new QueryHypothesis(null, "Doris Lessing", [], [], null, null)
        {
            Intent = QueryIntent.Topic, Subjects = ["cats"],
            Suggestions = [new BookSuggestion("The Cat Who Went to Paris", "Peter Gethers"), new BookSuggestion("Particularly Cats", "Doris Lessing")],
        };
        var pool = new[]
        {
            MatchRankerTests.Work("OL1W", "On Cats", ["Doris Lessing"], 13) with { Subjects = ["Cats"] },
            MatchRankerTests.Work("OL2W", "The Cat Who Went to Paris", ["Peter Gethers"], 30) with { Subjects = ["Cats"] },
            MatchRankerTests.Work("OL3W", "Particularly Cats", ["Doris Lessing"], 9),
        };
        var llm = MatchRanker.Rank(QueryCleaner.Clean("cat books by doris lessing"), hypothesis, pool);
        llm.Ranked.Select(w => w.Work.WorkId).ShouldBe(["OL1W", "OL3W"], ignoreOrder: true); // the suggestion by another author is dropped

        // No author frame, but the typed name is an author in the pool: Dr. Seuss's far more popular cat book is by
        // another author the query names, so it ranks below Lessing's.
        var typed = Query("lessing books about cats", search: ["lessing", "cats"], topic: ["cats"]);
        var cats = new[]
        {
            MatchRankerTests.Work("OL1W", "On Cats", ["Doris Lessing"], 13) with { Subjects = ["Cats"] },
            MatchRankerTests.Work("OL4W", "The Cat in the Hat", ["Dr. Seuss"], 400) with { Subjects = ["Cats"] },
        };
        MatchRanker.Rank(typed, null, cats).Ranked.Select(w => w.Work.WorkId).ShouldBe(["OL1W", "OL4W"]);
        MatchRanker.Rank(Query("books about cats", search: ["cats"], topic: ["cats"]), null, cats).Ranked[0].Work.WorkId.ShouldBe("OL4W");
    }

    [Fact]
    public void No_evidence_backed_book_is_none_with_the_topic_hint()
    {
        var query = Query("books about bears", search: ["bears"], topic: ["bears"]);
        var r = MatchRanker.Rank(query, null, [MatchRankerTests.Work("OL1W", "Bear", ["Marian Engel"], 40)]); // a title, no subjects
        r.Ranked.ShouldBeEmpty();
        r.TopicLabel.ShouldBe("bears");
        var decision = StatusPolicy.Decide(r, JudgeVerdict.Off, 0.9);
        decision.Status.ShouldBe(MatchStatus.None);
        decision.Reason.ShouldBe(StatusReason.NoCandidates);
        decision.Hint.ShouldBe(StatusPolicy.TopicNoneHint);
    }

    [Fact]
    public void A_topic_request_skips_the_book_title_calls()
    {
        var query = QueryCleaner.Clean("find me a book about bears");
        var hypothesis = new QueryHypothesis("Winnie-the-Pooh", "A. A. Milne", [], [], null, "Winnie-ther-Puuh")
        {
            Intent = QueryIntent.Topic, Subjects = ["bears"], Suggestions = [new BookSuggestion("Corduroy", "Don Freeman")],
        };
        RetrievalLadder.AfterInterpretation(query, hypothesis).Select(r => r.Step).ShouldBe([CatalogSteps.Topic, CatalogSteps.Suggestions]);
        RetrievalLadder.AfterBroad(query, hypothesis, [], [CatalogSteps.Broad]).ShouldBeEmpty();
        var book = hypothesis with { Intent = QueryIntent.Book };
        RetrievalLadder.AfterInterpretation(query, book).Select(r => r.Step).ShouldBe([CatalogSteps.Hypothesis, CatalogSteps.OriginalTitle]);
    }

    [Fact]
    public void A_preferred_audience_or_form_reorders_but_never_filters() // "picture books about bears"
    {
        var c = PoolFixtures.Case("TOPIC:bears:off");
        var plain = QueryCleaner.Clean(c.Query);
        var before = Replay.Run(c, plain, RankerOptions.Default).Ranking.Ranked.Select(w => w.Work.WorkId).ToList();
        var after = Replay.Run(c, plain with { TopicPreferences = ["picture books"] }, RankerOptions.Default).Ranking.Ranked
            .Select(w => w.Work.WorkId).ToList();
        after.ShouldBe(before, ignoreOrder: true);
        after.IndexOf("OL4114525W").ShouldBeLessThan(before.IndexOf("OL4114525W")); // Corduroy: "Picture books"
    }

    private static ReplayResult Run(string id)
    {
        var c = PoolFixtures.Case(id);
        return Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
    }

    private static CleanedQuery Query(string text, IReadOnlyList<string> search, IReadOnlyList<string> topic)
    {
        var all = FindThatBook.Core.Normalization.TextFolding.Tokens(text);
        return new CleanedQuery
        {
            Raw = text, Text = text, All = all, Search = search,
            Optional = all.Where(t => !search.Contains(t)).ToHashSet(StringComparer.Ordinal),
            Head = [], SoftTokens = new HashSet<string>(), Intents = new HashSet<EditionIntent>(), RemovedNoise = [], Topic = topic,
        };
    }
}
