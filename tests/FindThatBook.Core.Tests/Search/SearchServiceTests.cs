using FindThatBook.Core.Explanations;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;
using FindThatBook.Core.Search;

namespace FindThatBook.Core.Tests.Search;

/// <summary>
/// Orchestration only: the real cleaner, ranker, ladder and status policy run over hand-written fakes of the
/// four ports, so these tests pin what SearchService promises (budget, merge order, failure handling, deadline,
/// judge never reorders) without testing the matching rules again.
/// </summary>
public sealed class SearchServiceTests
{
    private static readonly CandidateWork Hobbit = Work("OL27482W", "The Hobbit", 482, ["J.R.R. Tolkien"], ["OL26320A"]);
    private static readonly CandidateWork HobbitGraphic = Work("OL219602W", "The Hobbit", 10,
        ["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"], ["OL2453782A", "OL758306A", "OL26320A"],
        contributors: ["Charles Dixon (Adapter)", "Sean Deming (Adapter)"]);
    private static readonly CandidateWork Huck = Work("OL53908W", "Adventures of Huckleberry Finn", 2623, ["Mark Twain"], ["OL18319A"]);
    private static readonly CandidateWork HuckRahr = Work("OL900001W", "Huckleberry Rahr", 1, ["Mark Rahr"], ["OL900001A"]);

    [Fact]
    public async Task Search_ClearTitleAndAuthor_ReturnsTierAMatchFromOneCall()
    {
        // Fewer than 3 distinct works after dedupe is a "weak" pool that triggers a rescue call (REC weak_pool);
        // the graphic novel folds into the canonical Hobbit, so the pool needs other works too.
        var annotated = Work("OL27479W", "The Annotated Hobbit", 25, ["J.R.R. Tolkien", "Douglas A. Anderson"], ["OL26320A", "OL2629025A"]);
        var companion = Work("OL3W", "The Hobbit Companion", 12, ["David Day"], ["OL3A"]);
        var catalog = new FakeCatalog().On(q => q.Q == "tolkien hobbit", Hobbit, HobbitGraphic, annotated, companion);
        var outcome = await Service(catalog).SearchAsync("tolkien hobbit illustrated deluxe 1937", null, Ct);

        outcome.Status.ShouldBe(MatchStatus.Match);
        outcome.Items[0].Work.Work.WorkId.ShouldBe("OL27482W");
        outcome.Items[0].Work.Tier.ShouldBe(MatchTier.A);
        outcome.Trace.CatalogCalls.Select(c => c.Step).ShouldBe([CatalogSteps.Broad]);
        outcome.Degraded.ShouldNotContain(DegradedReason.CatalogPartial);
    }

    [Fact]
    public async Task Search_NothingSearchable_ReturnsNoneWithoutCallingAnything()
    {
        var catalog = new FakeCatalog();
        var interpreter = new FakeInterpreter(null);
        var outcome = await Service(catalog, interpreter).SearchAsync("?!? ... ###", null, Ct);

        outcome.Status.ShouldBe(MatchStatus.None);
        outcome.Reason.ShouldBe(StatusReason.NoSearchableTerms);
        catalog.Requests.ShouldBeEmpty();
        interpreter.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Search_NeverExceedsTheCatalogBudget_EvenWhenEveryStepIsWeak()
    {
        // One junk hit for every request keeps the pool weak, so the ladder keeps asking for more.
        var junk = Work("OL1W", "Notebook", 1, ["Nobody"], ["OL1A"]);
        var catalog = new FakeCatalog().On(_ => true, junk);
        var hypothesis = new QueryHypothesis("The Purple Elephant Opera", "Ann Example", ["Ziggy Stardust", "Major Tom"], [], null, "Der lila Elefant");
        var options = new SearchOptions { MaxCatalogCalls = 3 };

        var outcome = await Service(catalog, new FakeInterpreter(hypothesis), options: options)
            .SearchAsync("ziggy major purple elephant opera", null, Ct);

        catalog.Requests.Count.ShouldBeLessThanOrEqualTo(3);
        outcome.Trace.CatalogCalls.Count.ShouldBe(catalog.Requests.Count);
        outcome.Trace.CatalogCallBudget.ShouldBe(3);
        outcome.Trace.CatalogCalls.Select(c => c.Step).ShouldBeUnique();
    }

    [Fact]
    public async Task Search_PoolMergesInStepOrder_NotCompletionOrder()
    {
        // The hypothesis call answers first in one run and last in the other; the ranking must not change.
        var hypothesis = new QueryHypothesis("Adventures of Huckleberry Finn", "Mark Twain", [], [], null, null);

        async Task<IReadOnlyList<string>> RunAsync(TimeSpan broadDelay, TimeSpan hypothesisDelay)
        {
            var catalog = new FakeCatalog()
                .On(q => q.Q == "mark huckleberry", broadDelay, HuckRahr, Huck)
                .On(q => q.Title is not null, hypothesisDelay, Huck, HuckRahr);
            var outcome = await Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("mark huckleberry", null, Ct);
            return outcome.Ranking.Ranked.Select(r => $"{r.Work.WorkId}:{r.Tier}:{r.Score:F4}")
                .Concat(outcome.Trace.CatalogCalls.Select(c => "step:" + c.Step)) // meta order must not depend on timing either
                .ToList();
        }

        var slowBroad = await RunAsync(TimeSpan.FromMilliseconds(150), TimeSpan.Zero);
        var slowHypothesis = await RunAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(150));
        slowBroad.ShouldBe(slowHypothesis);
        slowBroad[0].ShouldStartWith("OL53908W");
    }

    [Fact]
    public async Task Search_OpenLibraryRejectsShortQ_ReplansAsTitleSearch()
    {
        var it = Work("OL81613W", "It", 211, ["Stephen King"], ["OL2162284A"]);
        var catalog = new FakeCatalog()
            .Throw(q => q.Q is not null, new CatalogQueryRejectedException("Query too short", 1))
            .On(q => q.Title is not null, it);

        var outcome = await Service(catalog).SearchAsync("it", null, Ct);

        outcome.Items.ShouldNotBeEmpty();
        outcome.Items[0].Work.Work.WorkId.ShouldBe("OL81613W");
        catalog.Requests.ShouldAllBe(q => q.Q == null || q.Q.Length >= 3, "a q shorter than 3 characters is never sent (DE-05)");
    }

    [Fact]
    public async Task Search_EveryShapeRejected_IsNoneNotAnError()
    {
        var catalog = new FakeCatalog().Throw(_ => true, new CatalogQueryRejectedException("rejected", 1));
        var outcome = await Service(catalog).SearchAsync("tolkien hobbit", null, Ct);

        outcome.Status.ShouldBe(MatchStatus.None);
        outcome.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_FirstCallFailsAndNothingElseAnswered_PropagatesCatalogException()
    {
        var catalog = new FakeCatalog().Throw(_ => true, new CatalogUnavailableException("503 after retries", 3, TimeSpan.FromSeconds(15)));

        var ex = await Should.ThrowAsync<CatalogUnavailableException>(() => Service(catalog).SearchAsync("tolkien hobbit", null, Ct));
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Search_FirstCallFailsButHypothesisCallAnswers_ReturnsPartialResults()
    {
        var hypothesis = new QueryHypothesis("Adventures of Huckleberry Finn", "Mark Twain", [], [], null, null);
        var catalog = new FakeCatalog()
            .Throw(q => q.Q is not null, new CatalogTimeoutException("timed out", 3))
            .On(q => q.Title is not null, Huck);

        var outcome = await Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("mark huckleberry", null, Ct);

        outcome.Items[0].Work.Work.WorkId.ShouldBe("OL53908W");
        outcome.Degraded.ShouldContain(DegradedReason.CatalogPartial);
    }

    [Fact]
    public async Task Search_FallbackCallFails_KeepsWhatItHasAndStopsTheLadder()
    {
        var weak = Work("OL2W", "Scout, Atticus & Boo", 3, ["Mary McDonagh Murphy"], ["OL2A"]);
        var hypothesis = new QueryHypothesis(null, null, ["Scout Finch", "Atticus Finch"], [], null, null);
        var catalog = new FakeCatalog()
            .On(q => q.Q == "scout atticus", weak)
            .Throw(_ => true, new CatalogUnavailableException("circuit open", 0));

        var outcome = await Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("scout atticus", null, Ct);

        outcome.Items.ShouldNotBeEmpty();
        outcome.Degraded.ShouldContain(DegradedReason.CatalogPartial);
        outcome.Trace.CatalogCalls.Count(c => c.Outcome == CatalogCallOutcome.Failed).ShouldBe(1, "the ladder stops after the first outage");
    }

    [Fact]
    public async Task Search_DeadlinePassesDuringFallbacks_ReturnsResultsInHand()
    {
        var weak = Work("OL2W", "Scout, Atticus & Boo", 3, ["Mary McDonagh Murphy"], ["OL2A"]);
        var hypothesis = new QueryHypothesis(null, null, ["Scout Finch", "Atticus Finch"], [], null, null);
        var catalog = new FakeCatalog()
            .On(q => q.Q == "scout atticus", weak)
            .On(_ => true, TimeSpan.FromSeconds(30));
        var options = new SearchOptions { RequestDeadline = TimeSpan.FromMilliseconds(1500), PostRetrievalReserve = TimeSpan.FromMilliseconds(500) };

        var started = TimeProvider.System.GetTimestamp();
        var outcome = await Service(catalog, new FakeInterpreter(hypothesis), options: options).SearchAsync("scout atticus", null, Ct);

        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
        outcome.Items.ShouldNotBeEmpty();
        outcome.Degraded.ShouldContain(DegradedReason.DeadlineReached);
    }

    [Fact]
    public async Task Search_CallCutOffByTheDeadline_ReportsTheRequestsItAlreadySent()
    {
        var weak = Work("OL2W", "Scout, Atticus & Boo", 3, ["Mary McDonagh Murphy"], ["OL2A"]);
        var hypothesis = new QueryHypothesis(null, null, ["Scout Finch", "Atticus Finch"], [], null, null);
        var catalog = new FakeCatalog { CancelledAttempts = 2 }
            .On(q => q.Q == "scout atticus", weak)
            .On(_ => true, TimeSpan.FromSeconds(30));
        var options = new SearchOptions { RequestDeadline = TimeSpan.FromMilliseconds(1500), PostRetrievalReserve = TimeSpan.FromMilliseconds(500) };

        var outcome = await Service(catalog, new FakeInterpreter(hypothesis), options: options).SearchAsync("scout atticus", null, Ct);

        var cutOff = outcome.Trace.CatalogCalls.Single(c => c.Outcome == CatalogCallOutcome.Deadline);
        cutOff.Attempts.ShouldBe(2, "a measurement, not a hard-coded 0");
    }

    [Fact]
    public async Task Search_DeadlinePassesBeforeAnyResult_ThrowsSearchTimeout()
    {
        var catalog = new FakeCatalog().On(_ => true, TimeSpan.FromSeconds(30));
        var options = new SearchOptions { RequestDeadline = TimeSpan.FromMilliseconds(800), PostRetrievalReserve = TimeSpan.FromMilliseconds(200) };

        await Should.ThrowAsync<SearchTimeoutException>(() => Service(catalog, options: options).SearchAsync("tolkien hobbit", null, Ct));
    }

    [Fact]
    public async Task Search_EmptyBroadAndTimedOutHypothesis_ThrowsSearchTimeout()
    {
        var hypothesis = new QueryHypothesis("Adventures of Huckleberry Finn", "Mark Twain", [], [], null, null);
        var catalog = new FakeCatalog().On(q => q.Q == "mark huckleberry", [])
            .On(_ => true, TimeSpan.FromSeconds(30));
        var options = new SearchOptions { RequestDeadline = TimeSpan.FromMilliseconds(600), PostRetrievalReserve = TimeSpan.FromMilliseconds(200) };

        await Should.ThrowAsync<SearchTimeoutException>(() => Service(catalog, new FakeInterpreter(hypothesis), options: options)
            .SearchAsync("mark huckleberry", null, Ct));
    }

    [Fact]
    public async Task Search_LlmUnavailable_UsesHeuristicArmAndSaysWhy()
    {
        var catalog = new FakeCatalog().On(q => q.Q == "tolkien hobbit", Hobbit);
        var interpreter = new FakeInterpreter(null, LlmCallStatus.RateLimited);

        var outcome = await Service(catalog, interpreter).SearchAsync("tolkien hobbit", null, Ct);

        outcome.InterpretationSource.ShouldBe(InterpretationSource.Heuristic);
        outcome.Degraded.ShouldContain(DegradedReason.InterpretationFallback);
        outcome.Items[0].Work.Work.WorkId.ShouldBe("OL27482W");
        outcome.Trace.LlmCalls.ShouldContain(c => c.Operation == "interpret" && c.Status == LlmCallStatus.RateLimited);
    }

    [Fact]
    public async Task Search_JudgeDisabled_ReportsOffAndKeepsCodeStatus()
    {
        var catalog = new FakeCatalog().On(q => q.Q == "tolkien hobbit", Hobbit, HobbitGraphic);
        var outcome = await Service(catalog, judge: new FakeJudge(null)).SearchAsync("tolkien hobbit", null, Ct);

        outcome.Judge.Status.ShouldBe(JudgeStatus.Off);
        outcome.Status.ShouldBe(MatchStatus.Match);
    }

    [Fact]
    public async Task Search_JudgeDisagrees_NeverReordersAndFlagsItsPick()
    {
        var other = Work("OL3W", "The Hobbit Companion", 12, ["David Day"], ["OL3A"]);
        var catalog = new FakeCatalog().On(q => q.Q == "tolkien hobbit", Hobbit, HobbitGraphic, other);
        var withoutJudge = await Service(catalog).SearchAsync("tolkien hobbit", null, Ct);
        var judge = new FakeJudge(new JudgeVerdict(JudgeStatus.Disagreed, "OL3W", 0.99));

        var withJudge = await Service(catalog, judge: judge).SearchAsync("tolkien hobbit", null, Ct);

        judge.Calls.ShouldBe(1);
        withJudge.Ranking.Ranked.Select(r => r.Work.WorkId).ShouldBe(withoutJudge.Ranking.Ranked.Select(r => r.Work.WorkId));
        withJudge.Items[0].Work.Work.WorkId.ShouldBe("OL27482W");
        withJudge.Status.ShouldNotBe(MatchStatus.Match);
        withJudge.Items.Where(i => i.SecondOpinion).Select(i => i.Work.Work.WorkId).ShouldBe(["OL3W"]);
    }

    [Fact]
    public async Task Search_SameInputsTwice_GiveIdenticalRankingAndStatus()
    {
        var hypothesis = new QueryHypothesis("Adventures of Huckleberry Finn", "Mark Twain", [], [], null, null);
        var catalog = new FakeCatalog().On(q => q.Q is not null, HuckRahr, Huck).On(_ => true, Huck);

        var first = await Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("mark huckleberry", null, Ct);
        var second = await Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("mark huckleberry", null, Ct);

        second.Status.ShouldBe(first.Status);
        second.Reason.ShouldBe(first.Reason);
        second.Ranking.Ranked.Select(r => (r.Work.WorkId, r.Tier, r.Score))
            .ShouldBe(first.Ranking.Ranked.Select(r => (r.Work.WorkId, r.Tier, r.Score)));
    }

    [Fact]
    public async Task Search_StatusNone_ReturnsNoCandidatesAndSpendsNoExplanationCall()
    {
        var junk = Work("OL1W", "Notebook", 1, ["Nobody"], ["OL1A"]);
        var catalog = new FakeCatalog().On(_ => true, junk);
        var writer = new CountingWriter();

        var outcome = await Service(catalog, writer: writer).SearchAsync("purple elephant opera mars", null, Ct);

        outcome.Status.ShouldBe(MatchStatus.None);
        outcome.Items.ShouldBeEmpty();
        writer.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Search_EmptyPoolAfterLaterOutage_IsAnOutageNotNone()
    {
        // Call 1 honestly finds nothing; every later call fails. Claiming "no such book" would be wrong.
        var hypothesis = new QueryHypothesis("Adventures of Huckleberry Finn", "Mark Twain", [], [], null, null);
        var catalog = new FakeCatalog()
            .On(q => q.Q == "mark huckleberry")
            .Throw(_ => true, new CatalogUnavailableException("503 after retries", 3));

        await Should.ThrowAsync<CatalogUnavailableException>(
            () => Service(catalog, new FakeInterpreter(hypothesis)).SearchAsync("mark huckleberry", null, Ct));
    }

    [Fact]
    public async Task Search_InterpreterOutlivesTheDeadline_KeepsTheCatalogResults()
    {
        var catalog = new FakeCatalog().On(q => q.Q == "tolkien hobbit", Hobbit);
        var interpreter = new FakeInterpreter(null, delay: TimeSpan.FromSeconds(30));
        var options = new SearchOptions { RequestDeadline = TimeSpan.FromMilliseconds(800), PostRetrievalReserve = TimeSpan.FromMilliseconds(200) };

        var outcome = await Service(catalog, interpreter, options: options).SearchAsync("tolkien hobbit", null, Ct);

        outcome.Items.ShouldNotBeEmpty();
        outcome.Items[0].Work.Work.WorkId.ShouldBe("OL27482W");
        outcome.InterpretationSource.ShouldBe(InterpretationSource.Heuristic);
        outcome.Degraded.ShouldContain(DegradedReason.DeadlineReached);
    }

    [Fact]
    public void Pool_WorkSeenFirstWithoutSubjects_GetsThemFromALaterTopicCall()
    {
        var pool = new CandidatePool();
        pool.Add([Work("OL832133W", "We're Going on a Bear Hunt", 119, ["Michael Rosen"], ["OL1A"])]);
        pool.Add([Work("OL832133W", "We're Going on a Bear Hunt", 119, ["Michael Rosen"], ["OL1A"]) with { Subjects = ["Bears", "Hunting"] }]);

        pool.Works.Single().Subjects.ShouldBe(["Bears", "Hunting"]);
    }

    // ---------- helpers and fakes ----------

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SearchService Service(FakeCatalog catalog, FakeInterpreter? interpreter = null, FakeJudge? judge = null,
        SearchOptions? options = null, IExplanationWriter? writer = null)
        => new(new InterpretationService(interpreter ?? new FakeInterpreter(null)), catalog,
            new ExplanationService(writer ?? new UnavailableWriter()), judge ?? new FakeJudge(null), options ?? new SearchOptions(), TimeProvider.System);

    private static CandidateWork Work(string id, string title, int editions, string[] authors, string[] keys, string[]? contributors = null) => new()
    {
        WorkId = id, Title = title, EditionCount = editions, AuthorNames = authors, AuthorKeys = keys, Contributors = contributors ?? [],
    };

    private sealed class FakeCatalog : IBookCatalog
    {
        private readonly List<(Func<CatalogQuery, bool> Match, TimeSpan Delay, CandidateWork[] Works, Exception? Error)> _rules = [];
        private readonly Lock _gate = new();
        public List<CatalogQuery> Requests { get; } = [];

        /// <summary>When set, a call cancelled mid-delay reports this many HTTP attempts, like the real adapter.</summary>
        public int? CancelledAttempts { get; init; }

        public FakeCatalog On(Func<CatalogQuery, bool> match, params CandidateWork[] works) => On(match, TimeSpan.Zero, works);

        public FakeCatalog On(Func<CatalogQuery, bool> match, TimeSpan delay, params CandidateWork[] works)
        {
            _rules.Add((match, delay, works, null));
            return this;
        }

        public FakeCatalog Throw(Func<CatalogQuery, bool> match, Exception error)
        {
            _rules.Add((match, TimeSpan.Zero, [], error));
            return this;
        }

        public async Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
        {
            lock (_gate) Requests.Add(query);
            var rule = _rules.FirstOrDefault(r => r.Match(query));
            if (rule.Match is null) return new CatalogResponse([], 0, new CatalogCallStats(false, 1, 0));
            if (rule.Delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(rule.Delay, ct);
                }
                catch (OperationCanceledException ex) when (CancelledAttempts is { } attempts)
                {
                    throw new CatalogCancelledException(attempts, ex, ct);
                }
            }
            if (rule.Error is not null) throw rule.Error;
            return new CatalogResponse(rule.Works, rule.Works.Length, new CatalogCallStats(false, 1, (long)rule.Delay.TotalMilliseconds));
        }
    }

    private sealed class FakeInterpreter(QueryHypothesis? hypothesis, LlmCallStatus status = LlmCallStatus.Ok, TimeSpan delay = default) : IQueryInterpreter
    {
        public int Calls { get; private set; }

        public async Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct)
        {
            Calls++;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            var effective = hypothesis is null && status == LlmCallStatus.Ok ? LlmCallStatus.NotConfigured : status;
            var call = new LlmCall("interpret", "fake", "fake-model", "interpret.v3", effective, false, null, null, 0);
            return new LlmResult<QueryHypothesis>(effective == LlmCallStatus.Ok ? hypothesis : null, call);
        }
    }

    private sealed class CountingWriter : IExplanationWriter
    {
        public int Calls { get; private set; }

        public Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LlmResult<IReadOnlyDictionary<string, string>>(null,
                new LlmCall("explain", "fake", "fake-model", "explain.v3", LlmCallStatus.ProviderError, false, null, null, 0)));
        }
    }

    private sealed class UnavailableWriter : IExplanationWriter
    {
        public Task<LlmResult<IReadOnlyDictionary<string, string>>> WriteAsync(IReadOnlyList<ExplanationFacts> facts, CancellationToken ct)
            => Task.FromResult(new LlmResult<IReadOnlyDictionary<string, string>>(null,
                new LlmCall("explain", "fake", "fake-model", "explain.v2", LlmCallStatus.NotConfigured, false, null, null, 0)));
    }

    private sealed class FakeJudge(JudgeVerdict? verdict) : IMatchJudge
    {
        public int Calls { get; private set; }
        public bool IsEnabled => verdict is not null;

        public Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(verdict!);
        }
    }
}
