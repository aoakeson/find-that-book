using FindThatBook.Core.Interpretation;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Tests.Interpretation;

public sealed class InterpretationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static CleanedQuery Query(string raw, int? year = null) => new()
    {
        Raw = raw,
        Text = raw,
        All = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Search = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        Optional = new HashSet<string>(),
        Head = [],
        Year = year,
        SoftTokens = new HashSet<string>(),
        Intents = new HashSet<EditionIntent>(),
        RemovedNoise = [],
    };

    private static LlmCall Call(LlmCallStatus status) =>
        new("interpret", "gemini", "gemini-3.5-flash-lite", "interpret.v3", status, false, 240, 80, 700);

    private sealed class FakeInterpreter(Func<Task<LlmResult<QueryHypothesis>>> answer) : IQueryInterpreter
    {
        public Task<LlmResult<QueryHypothesis>> InterpretAsync(CleanedQuery query, CancellationToken ct) => answer();
    }

    private static InterpretationService Service(QueryHypothesis? value, LlmCallStatus status) =>
        new(new FakeInterpreter(() => Task.FromResult(new LlmResult<QueryHypothesis>(value, Call(status)))));

    [Fact]
    public async Task Ok_answer_is_sanitized_and_marked_llm()
    {
        var raw = new QueryHypothesis("  The Hobbit ", "J.R.R. Tolkien", [], [" illustrated "], "The Hobbit", null);
        var outcome = await Service(raw, LlmCallStatus.Ok).InterpretAsync(Query("tolkien hobbit illustrated"), Ct);

        outcome.Source.ShouldBe(InterpretationSource.Llm);
        outcome.Degraded.ShouldBeNull();
        outcome.Hypothesis!.Title.ShouldBe("The Hobbit");
        outcome.Hypothesis.EnglishTitle.ShouldBeNull(); // same as the title
        outcome.Hypothesis.Keywords.ShouldBe(["illustrated"]);
        outcome.Call!.Status.ShouldBe(LlmCallStatus.Ok);
    }

    [Theory]
    [InlineData(LlmCallStatus.NotConfigured, DegradedReason.AiNotConfigured)]
    [InlineData(LlmCallStatus.BudgetExhausted, DegradedReason.AiBudgetExhausted)]
    [InlineData(LlmCallStatus.RateLimited, DegradedReason.InterpretationFallback)]
    [InlineData(LlmCallStatus.Timeout, DegradedReason.InterpretationFallback)]
    [InlineData(LlmCallStatus.ProviderError, DegradedReason.InterpretationFallback)]
    [InlineData(LlmCallStatus.MalformedOutput, DegradedReason.InterpretationFallback)]
    [InlineData(LlmCallStatus.Truncated, DegradedReason.InterpretationFallback)]
    [InlineData(LlmCallStatus.Refused, DegradedReason.InterpretationFallback)]
    public async Task Failure_is_the_heuristic_arm_with_a_reason(LlmCallStatus status, DegradedReason reason)
    {
        var outcome = await Service(null, status).InterpretAsync(Query("dickens"), Ct);
        outcome.Hypothesis.ShouldBeNull();
        outcome.Source.ShouldBe(InterpretationSource.Heuristic);
        outcome.Degraded.ShouldBe(reason);
        outcome.Call!.Status.ShouldBe(status);
    }

    [Fact]
    public async Task Ok_without_a_value_is_a_fallback()
    {
        var outcome = await Service(null, LlmCallStatus.Ok).InterpretAsync(Query("dickens"), Ct);
        outcome.Hypothesis.ShouldBeNull();
        outcome.Degraded.ShouldBe(DegradedReason.InterpretationFallback);
    }

    [Fact]
    public async Task A_throwing_adapter_still_returns_the_heuristic_arm()
    {
        var service = new InterpretationService(new FakeInterpreter(() => throw new InvalidOperationException("boom")));
        var outcome = await service.InterpretAsync(Query("dickens"), Ct);
        outcome.Hypothesis.ShouldBeNull();
        outcome.Degraded.ShouldBe(DegradedReason.InterpretationFallback);
        outcome.Call!.Status.ShouldBe(LlmCallStatus.ProviderError);
        outcome.Call.Detail.ShouldBe("InvalidOperationException");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var service = new InterpretationService(new FakeInterpreter(() => Task.FromCanceled<LlmResult<QueryHypothesis>>(cts.Token)));
        await Should.ThrowAsync<OperationCanceledException>(() => service.InterpretAsync(Query("dickens"), cts.Token));
    }
}
