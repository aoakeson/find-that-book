using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;
using FindThatBook.Eval.Replay;
using FindThatBook.Eval.Reports;
using FindThatBook.Eval.Running;
using FindThatBook.Eval.Scoring;

namespace FindThatBook.Api.Tests.Golden;

/// <summary>The eval's comparison arms: hypothesis replay, template-only explanations, record-on-miss cache, ranker ablation, battle scoring.</summary>
public sealed class ComparisonArmsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ftb-arms-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllLines(path, lines);
        return path;
    }

    // ---- hypothesis replay ----

    [Fact]
    public async Task Replay_ResearchCacheLine_IsSanitizedByThePipelinesInterpretationService()
    {
        // Research-cache hyp_v3.jsonl shape: raw Codex output, here with a character the patron never typed (AI-04).
        var path = File("hyp_v3.jsonl", """{"query":"tolkien hobbit","hyp":{"title":"The Hobbit","author":"J.R.R. Tolkien","characters":["Gandalf"],"keywords":[],"english_title":null,"original_title":null}}""");
        var service = new InterpretationService(new ReplayQueryInterpreter(HypothesisStore.Load(path)));

        var outcome = await service.InterpretAsync(QueryCleaner.Clean("tolkien  hobbit "), TestContext.Current.CancellationToken);

        outcome.Source.ShouldBe(InterpretationSource.Llm);
        outcome.Hypothesis!.Title.ShouldBe("The Hobbit");
        outcome.Hypothesis.Characters.ShouldBeEmpty(); // sanitizer dropped the invented character
        outcome.Call!.CacheHit.ShouldBeTrue();          // nothing sent, nothing billed
        outcome.Call.Model.ShouldContain("prompt v3");
    }

    [Fact]
    public async Task Replay_UnknownQuery_RunsLlmOffAndIsReportedAsHypothesisMiss()
    {
        var store = HypothesisStore.Load(File("hyp_v3.jsonl", """{"query":"x","hyp":null}"""));
        var probe = new QueryProbe("R1-0001");

        InterpretationOutcome outcome;
        using (EvalContext.Begin(probe))
            outcome = await new InterpretationService(new ReplayQueryInterpreter(store)).InterpretAsync(QueryCleaner.Clean("dune herbert"), TestContext.Current.CancellationToken);

        outcome.Hypothesis.ShouldBeNull();
        outcome.Source.ShouldBe(InterpretationSource.Heuristic);
        outcome.Call!.Detail.ShouldBe(ReplayQueryInterpreter.MissDetail);
        probe.HypothesisMisses.ShouldBe(["dune herbert"]);
    }

    [Fact]
    public void Load_EvalResultsRows_ReplaysHypothesesAndFailuresAsRecorded()
    {
        static string Row(string id, string query, QueryHypothesis? h, LlmCallStatus status, bool interpreted = true) => JsonSerializer.Serialize(new ResultRow
        {
            Id = id, Query = query, Category = "c", Expected = new ExpectedView([], [], [], [], null, [], null, null), Status = "match",
            Pass = new PassView(true, true, true, true, true, false, 1),
            Meta = new RowMeta
            {
                Hypothesis = h,
                LlmCalls = interpreted ? [new LlmCall("interpret", "gemini", "gemini-3.5-flash-lite", "interpret.v3", status, false, 10, 5, 100)] : [],
            },
        }, EvalJson.Options);
        var path = File("results.jsonl",
            Row("a", "twilight meyer", new QueryHypothesis("Twilight", "Stephenie Meyer", [], [], null, null), LlmCallStatus.Ok),
            Row("b", "mark huckleberry", null, LlmCallStatus.RateLimited),
            Row("c", "?!? ... ###", null, LlmCallStatus.Ok, interpreted: false));

        var store = HypothesisStore.Load(path);

        store.Count.ShouldBe(2); // the rejected row never reached the interpreter
        store.Find("twilight meyer")!.Hypothesis!.Author.ShouldBe("Stephenie Meyer");
        store.Find("twilight meyer")!.Model.ShouldBe("gemini/gemini-3.5-flash-lite");
        store.Find("mark huckleberry")!.Status.ShouldBe(LlmCallStatus.RateLimited); // a failed call replays as a failure
    }

    [Fact]
    public async Task TemplateOnlyWriter_NeverProducesSentences()
    {
        var r = await new TemplateOnlyExplanationWriter().WriteAsync([], TestContext.Current.CancellationToken);

        r.Value.ShouldBeNull();
        r.Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
        r.Call.Detail.ShouldBe(TemplateOnlyExplanationWriter.Detail);
    }

    // ---- record-on-miss catalog cache ----

    private sealed class CountingCatalog(Action onCall) : IBookCatalog
    {
        public int Calls { get; private set; }

        public Task<CatalogResponse> SearchAsync(CatalogQuery query, CancellationToken ct)
        {
            Calls++;
            onCall();
            return Task.FromResult(new CatalogResponse([new CandidateWork { WorkId = "OL1W", Title = "From the backend" }], 1, new CatalogCallStats(false, 1, 5)));
        }
    }

    [Fact]
    public async Task RecordOnMiss_FirstRequestFetchesAndRecords_SecondIsAnsweredFromTheRecording()
    {
        var sink = new RecordingSink();
        var recording = Path.Combine(_dir, "search_x.json");
        var backend = new CountingCatalog(() =>
        {
            // What the recording handler does inside the adapter: save the body, report the file to the sink.
            System.IO.File.WriteAllText(recording, """{"url":"http://127.0.0.1:8090/search.json?q=dune+herbert&limit=20","body":{"numFound":1,"docs":[{"key":"/works/OL893415W","title":"Dune"}]}}""");
            sink.Saved.Enqueue(recording);
        });
        var cache = new ReplayCatalog(RecordedResponseIndex.Load([Path.Combine(_dir, "empty")]), () => backend, sink: sink);
        var query = new CatalogQuery { Q = "dune herbert" };

        var first = await cache.SearchAsync(query, TestContext.Current.CancellationToken);
        var second = await cache.SearchAsync(query, TestContext.Current.CancellationToken);

        backend.Calls.ShouldBe(1);
        cache.Fills.ShouldBe(1);
        first.Works.Single().Title.ShouldBe("Dune"); // mapped from the recording, like every later arm
        first.Stats.Attempts.ShouldBe(1);           // the fetch's real stats
        second.Works.Single().WorkId.ShouldBe("OL893415W");
        second.Stats.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task RecordOnMiss_PersonQueryOnTheMirror_IsAnsweredEmptyWithoutABackendCall()
    {
        var backend = new CountingCatalog(() => { });
        var cache = new ReplayCatalog(RecordedResponseIndex.Load([Path.Combine(_dir, "empty")]), () => backend, EvalHost.MirrorCannotAnswer);

        var r = await cache.SearchAsync(new CatalogQuery { Q = "person:(bilbo AND gandalf)", IncludePersons = true }, TestContext.Current.CancellationToken);

        r.Works.ShouldBeEmpty();
        backend.Calls.ShouldBe(0);
        cache.UnsupportedCalls.ShouldBe(1);
    }

    [Fact]
    public void FromTrace_PipelineTraceText_GivesTheSameKeyAsTheQuery()
    {
        var q = new CatalogQuery { Title = "the hobbit", Author = "tolkien", Sort = CatalogSort.Editions, Limit = 20, IncludePersons = true };

        RequestKey.FromTrace(q.ToString()).IndexKey.ShouldBe(RequestKey.From(q).IndexKey);
    }

    // ---- ranker arms ----

    [Fact]
    public void Switches_AreDiscoveredFromRankerOptions()
    {
        RankerArms.Switches.ShouldContain("SoftTokens");
        RankerArms.Switches.ShouldContain("TinyRecord");
        RankerArms.Switches.Count.ShouldBeGreaterThanOrEqualTo(14);
        RankerArms.OnIn("default", []).ShouldBe(RankerArms.Switches);
        RankerArms.OnIn("baseline", []).ShouldBeEmpty();
        RankerArms.OnIn("v3", []).ShouldBeEmpty(); // alias kept for old command lines
        RankerArms.OnIn("default", ["TinyRecord"]).ShouldNotContain("TinyRecord");
        RankerArms.Describe("default", ["TinyRecord", "Mashup"]).ShouldBe("default -TinyRecord -Mashup");
    }

    [Fact]
    public void Build_UnknownSwitch_ListsTheValidOnes() =>
        Should.Throw<ArgumentException>(() => RankerArms.Validate("default", ["NoSuchRule"])).Message.ShouldContain("SoftTokens");

    [Fact]
    public void Flips_RowsTheRuleWasWinningOrLosing_AreCountedAgainstTheDefaultArm()
    {
        static ResultRow Row(string id, bool top1, string status = "match") => new()
        {
            Id = id, Query = id, Category = "c", Expected = new ExpectedView([], [], [], [], null, [], null, null), Status = status,
            Pass = new PassView(true, top1, top1, true, status == "match" ? top1 : null, false, top1 ? 1 : null), Meta = new RowMeta(),
        };
        var stats = QualityStats.Compute([]);
        var baseArm = new AblateCommand.ArmResult(new("default", "default", []), stats, [Row("a", true), Row("b", false), Row("c", true)], "");
        var off = new AblateCommand.ArmResult(new("-X", "default", ["X"]), stats, [Row("a", false), Row("b", true), Row("c", true)], "");

        var (breaks, fixes) = AblateCommand.Flips(off, baseArm.Rows.ToDictionary(r => r.Id));

        breaks.Select(r => r.Id).ShouldBe(["a"]);
        fixes.Select(r => r.Id).ShouldBe(["b"]);
    }

    // ---- battle scoring ----

    [Fact]
    public void Hit_FoldedDuplicate_CountsOnlyWhereTheSetAsksForIt()
    {
        var work = ObservedWork.Of("OL2W", "Dune", ["Frank Herbert"], folded: ["OL1W"]);
        var golden = new Expectation { Statuses = ["match"], Top1 = ["OL1W"], Top5 = ["OL1W"] };

        CaseScorer.Hit(golden, work, top1: true).ShouldBeFalse();
        CaseScorer.Hit(golden with { CountFolded = true }, work, top1: true).ShouldBeTrue();
    }

    [Fact]
    public void Score_MatchOnAnotherRecordOfTheIntendedBook_IsDuplicateRecordNotSevere()
    {
        var e = new Expectation { Statuses = ["match"], Top1 = ["OL1W"], Top5 = ["OL1W"], DupTitles = ["Dune"], DupAuthors = ["Frank Herbert"] };

        var dup = CaseScorer.Score(e, new Observed("match", null, [ObservedWork.Of("OL9W", "Dune (Deluxe Edition)", ["Herbert, Frank"])]));
        var wrong = CaseScorer.Score(e, new Observed("match", null, [ObservedWork.Of("OL8W", "Dune Messiah", ["Frank Herbert"])]));

        dup.DupRecordMatch.ShouldBeTrue();
        dup.SevereMatch.ShouldBeFalse();
        dup.MatchCorrect.ShouldBe(false); // precision stays strict; the duplicate is reported separately
        wrong.SevereMatch.ShouldBeTrue();
        QualityStats.Compute([("match", dup), ("match", wrong)]).ShouldSatisfyAllConditions(
            q => q.SevereMatches.ShouldBe(1),
            q => q.DupRecordMatches.ShouldBe(1));
    }

    [Fact] // judge.py status_over: the right book as "match" where ambiguous was expected is a status error, not severe
    public void Score_RightBookAsMatchWhereAmbiguousExpected_IsAStatusErrorNotASevereMatch()
    {
        var e = new Expectation { Statuses = ["ambiguous"], AuthorKeys = ["OL1416857A"] };

        var s = CaseScorer.Score(e, new Observed("match", null, [ObservedWork.Of("OL1W", "The Midnight Library", ["Matt Haig"], ["OL1416857A"])]));

        s.Status.ShouldBe(false);
        s.SevereMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task RecordOnMiss_ShortRecording_IsUpgradedFromTheBackendInsteadOfTruncatingTheAnswer()
    {
        System.IO.File.WriteAllText(Path.Combine(_dir, "short.json"),
            """{"url":"http://127.0.0.1:8090/search.json?q=dune&limit=5","body":{"numFound":40,"docs":[{"key":"/works/OL1W","title":"Dune"}]}}""");
        var sink = new RecordingSink();
        var full = Path.Combine(_dir, "full", "search_full.json");
        var backend = new CountingCatalog(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, """{"url":"http://127.0.0.1:8090/search.json?q=dune&limit=20","body":{"numFound":40,"docs":[{"key":"/works/OL1W","title":"Dune"},{"key":"/works/OL2W","title":"Dune Messiah"}]}}""");
            sink.Saved.Enqueue(full);
        });
        var cache = new ReplayCatalog(RecordedResponseIndex.Load([_dir]), () => backend, sink: sink);

        var r = await cache.SearchAsync(new CatalogQuery { Q = "dune", Limit = 20 }, TestContext.Current.CancellationToken);

        backend.Calls.ShouldBe(1);
        r.Works.Count.ShouldBe(2);
    }

    // ---- topic rows ----

    private static readonly Expectation Bears = new()
    {
        Statuses = ["ambiguous"], ExpectedReason = "topic_search", TopicWords = ["bear", "ours", "astronom*", "space flight"], MinTopicHits = 3,
    };

    private static ObservedWork On(string id, params string[] subjects) => ObservedWork.Of(id, subjects: subjects);

    [Fact]
    public void Score_TopicRow_PassesOnReasonAndThreeOnTopicWorks()
    {
        var o = new Observed("ambiguous", null,
            [On("OL1W", "Bears -- Juvenile fiction"), On("OL2W", "Polar bear"), On("OL3W", "Cooking"), On("OL4W", "Ours (animaux)"), On("OL5W")],
            Reason: "topic_search");

        var s = CaseScorer.Score(Bears, o);

        s.ShouldSatisfyAllConditions(x => x.Top1.ShouldBe(true), x => x.Top5.ShouldBe(true), x => x.Status.ShouldBe(true));
        CaseScorer.Score(Bears, o with { Reason = "weak_evidence" }).Status.ShouldBe(false); // ambiguous for another reason
    }

    [Theory] // word matching: plural ok, a different word that merely starts with it is not, '*' is a prefix, phrases stay phrases
    [InlineData("Bears", true)]
    [InlineData("Bearded dragons", false)]
    [InlineData("Hours of devotion", false)]
    [InlineData("Astronomy -- Juvenile literature", true)]
    [InlineData("Space flight to the moon", true)]
    [InlineData("Flight -- Space", false)]
    public void TopicHit_SubjectWords(string subject, bool hit) => CaseScorer.TopicHit(Bears, On("OL1W", subject)).ShouldBe(hit);

    [Fact]
    public void Load_TopicSet_HasTopicRowsAndStrictControls()
    {
        var v1 = DatasetLoader.Load("topic-v1", allowHeldOut: false);
        var d = DatasetLoader.Load("topic", allowHeldOut: false);

        v1.Cases.Count(c => c.Expected.IsTopicRow).ShouldBe(21);
        d.Version.ShouldBe(2);
        d.Cases.Count(c => c.Expected.IsTopicRow).ShouldBe(24);
        d.Cases.Single(c => c.Category == "topic_author").ShouldSatisfyAllConditions(
            c => c.Expected.Answerable.ShouldBeFalse(), c => c.Expected.Statuses.ShouldBe(["ambiguous", "none"]));
        var controls = d.Cases.Where(c => c.Category == "topic_control").ToList();
        controls.Count.ShouldBe(13);
        controls.ShouldAllBe(c => c.Expected.LenientTitle == null && c.Expected.Top1.Count > 0); // golden-style ids only
        d.Cases.Single(c => c.Query == "find me aciman").Expected.Top1.ShouldBe(["OL20454252W"]);
    }

    [Fact]
    public void LoadBattle_RoundOne_MapsBookAuthorAndNoneRowsLikeJudgePy()
    {
        var path = Path.Combine(RepoPaths.Research, "11-battle-test", "rounds", "r1", "resolved.jsonl");
        if (!System.IO.File.Exists(path)) Assert.Skip("the battle rounds are in the optional research archive, which is not part of this repo (set FTB_RESEARCH to run this test)");

        var d = DatasetLoader.Load("battle:r1", allowHeldOut: false);
        var raw = System.IO.File.ReadLines(path).Select(l => JsonNode.Parse(l)!).ToDictionary(n => n["qid"]!.GetValue<string>());

        d.Cases.Count.ShouldBe(raw.Count);
        d.Label!.ShouldBe(DatasetLoader.BattleLabel);
        d.Cases.ShouldAllBe(c => c.InSample);
        foreach (var c in d.Cases)
        {
            var kind = raw[c.Id]["kind"]!.GetValue<string>();
            if (kind == "author") c.Expected.IsAuthorRow.ShouldBeTrue(c.Id);
            else if (kind == "none") c.Expected.Answerable.ShouldBeFalse(c.Id);
            else c.Expected.ShouldSatisfyAllConditions(e => e.CountFolded.ShouldBeTrue(), e => e.Top1.ShouldNotBeEmpty());
        }
    }
}
