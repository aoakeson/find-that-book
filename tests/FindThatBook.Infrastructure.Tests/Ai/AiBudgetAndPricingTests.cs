using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FindThatBook.Infrastructure.Tests.Ai;

public sealed class AiBudgetAndPricingTests
{
    private const string Interpret = "interpret";
    private const string Explain = "explain";

    private static (AiBudget Budget, FakeTimeProvider Time) Budget(int rpm = 0, decimal cap = 0, int cooldown = 30, int maxCooldown = 300,
        int reserve = 3, int anthropicRpm = 0)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 23, 59, 0, TimeSpan.Zero));
        var options = new AiOptions { Budget = new() { DailyCostCapUsd = cap, CooldownSeconds = cooldown, MaxCooldownSeconds = maxCooldown, ExplainReserve = reserve } };
        options.Gemini.RequestsPerMinute = rpm;
        options.Anthropic.RequestsPerMinute = anthropicRpm;
        return (new AiBudget(Options.Create(options), time), time);
    }

    [Fact]
    public void Defaults_fit_the_measured_gemini_free_tier()
    {
        var o = new AiOptions();
        o.Gemini.RequestsPerMinute.ShouldBe(15);
        o.Anthropic.RequestsPerMinute.ShouldBe(50);
        o.OpenAI.RequestsPerMinute.ShouldBe(50);
        o.Budget.ExplainReserve.ShouldBe(3);
    }

    [Fact]
    public void Sliding_window_allows_rpm_calls_per_minute()
    {
        var (budget, time) = Budget(rpm: 3);
        for (var i = 0; i < 3; i++) budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
        budget.TryAcquire("gemini", Interpret).ShouldBe(new BudgetDecision(false, LlmCallStatus.BudgetExhausted, "rpm"));
        time.Advance(TimeSpan.FromSeconds(59));
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Windows_are_per_provider()
    {
        var (budget, _) = Budget(rpm: 1, anthropicRpm: 2);
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeFalse();
        budget.TryAcquire("anthropic", Interpret).Allowed.ShouldBeTrue();
        budget.TryAcquire("anthropic", Interpret).Allowed.ShouldBeTrue();
        budget.TryAcquire("anthropic", Interpret).Allowed.ShouldBeFalse();
        budget.TryAcquire("openai", Interpret).Allowed.ShouldBeTrue(); // 0 = unlimited
    }

    [Theory]
    [InlineData(15, 3, 12)] // Gemini free tier: 12 explains, then the last 3 permits are interpretation's
    [InlineData(5, 3, 2)]
    [InlineData(5, 0, 5)]   // reserve off
    [InlineData(2, 3, 0)]   // a window smaller than the reserve: explanations never take it
    public void Explain_stops_while_interpretation_still_has_its_reserve(int rpm, int reserve, int explains)
    {
        var (budget, _) = Budget(rpm: rpm, reserve: reserve);
        var granted = 0;
        while (budget.TryAcquire("gemini", Explain).Allowed) granted++;
        granted.ShouldBe(explains);
        budget.TryAcquire("gemini", Explain).Detail.ShouldBe(explains == rpm ? "rpm" : "explain_reserve");
        var interprets = 0;
        while (budget.TryAcquire("gemini", Interpret).Allowed) interprets++;
        interprets.ShouldBe(rpm - explains);
    }

    [Fact]
    public void Cooldown_is_per_provider_and_bounded()
    {
        var (budget, time) = Budget(cooldown: 30, maxCooldown: 120);
        budget.StartCooldown("gemini", null).ShouldBe(TimeSpan.FromSeconds(30));
        budget.TryAcquire("gemini", Interpret).ShouldBe(new BudgetDecision(false, LlmCallStatus.BudgetExhausted, "cooldown"));
        budget.TryAcquire("anthropic", Interpret).Allowed.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(30));
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();

        budget.StartCooldown("gemini", TimeSpan.FromHours(1)).ShouldBe(TimeSpan.FromSeconds(120)); // a daily-quota 429 can say "come back tomorrow"
        budget.StartCooldown("gemini", TimeSpan.FromSeconds(5)); // a concurrent shorter 429 must not cut it
        time.Advance(TimeSpan.FromSeconds(60));
        budget.TryAcquire("gemini", Interpret).ShouldBe(new BudgetDecision(false, LlmCallStatus.BudgetExhausted, "cooldown"));
        time.Advance(TimeSpan.FromSeconds(60));
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Daily_spend_resets_at_utc_midnight()
    {
        var (budget, time) = Budget(cap: 0.01m);
        budget.RecordSpend(0.004m);
        budget.RecordSpend(null);
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
        budget.RecordSpend(0.006m);
        budget.TryAcquire("gemini", Interpret).ShouldBe(new BudgetDecision(false, LlmCallStatus.BudgetExhausted, "daily_cap"));
        time.Advance(TimeSpan.FromMinutes(1)); // 2026-10-25 00:00 UTC
        budget.SpentToday.ShouldBe(0m);
        budget.TryAcquire("gemini", Interpret).Allowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("gemini-3.5-flash-lite", 400L, 60L, 0.00027)]   // 400×0.30 + 60×2.50 per MTok
    [InlineData("claude-haiku-5-5", 1_000_000L, 1_000_000L, 0.60)]
    [InlineData("GPT-6-LUNA", 824L, 0L, 0.0000824)]           // the judge's choice-only median
    [InlineData("gpt-4.1-mini", 0L, 0L, 0.0)]                  // a cache hit
    public void Pricing_estimates_from_the_table(string model, long input, long output, double expected)
    {
        new AiPricing(Options.Create(new AiOptions())).Estimate(model, input, output).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Budget_estimate_falls_back_to_the_dearest_prices_only_for_unpriced_models()
    {
        var pricing = new AiPricing(Options.Create(new AiOptions()));
        pricing.BudgetEstimate("claude-haiku-5-5", 1_000_000, 0).ShouldBe(0.10m);             // priced: the real estimate
        pricing.BudgetEstimate("gemini-9-ultra", 1_000_000, 1_000_000).ShouldBe(2.00m + 10.00m); // dearest input + output
        pricing.Estimate("gemini-9-ultra", 1_000_000, 1_000_000).ShouldBeNull();
        pricing.BudgetEstimate("gemini-9-ultra", null, null).ShouldBeNull();
    }

    [Theory]
    [InlineData("gemini-9-ultra", 100L, 10L)]
    [InlineData("gemini-3.8-flash-lite", 100L, 10L)] // no prefix guessing: a different model
    [InlineData("", 100L, 10L)]
    [InlineData("gpt-4.1-mini", null, null)]
    public void Pricing_is_null_when_unknown(string model, long? input, long? output)
    {
        new AiPricing(Options.Create(new AiOptions())).Estimate(model, input, output).ShouldBeNull();
    }

    [Fact]
    public void Configuration_binds_over_the_defaults_and_keeps_case_insensitive_prices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Provider"] = "Anthropic",
            ["AI:Gemini:Model"] = "gemini-3.8-flash",
            ["AI:Gemini:MaxOutputTokens:Interpret"] = "768",
            ["AI:Pricing:my-model:InputPerMTok"] = "1",
            ["AI:Pricing:my-model:OutputPerMTok"] = "2",
        }).Build();
        var options = Resolve(config).Value;

        options.Gemini.Model.ShouldBe("gemini-3.8-flash");
        options.Gemini.OutputFloor("Interpret").ShouldBe(768);
        options.Gemini.OutputFloor("Explain").ShouldBe(1024);
        options.Anthropic.Model.ShouldBe("claude-haiku-5-5");
        options.OpenAI.Model.ShouldBe("gpt-4.1-mini");
        options.Interpret.TimeoutMs.ShouldBe(4000);
        options.Explain.TimeoutMs.ShouldBe(5000);
        options.Cache.TtlMinutes.ShouldBe(1440);
        var pricing = new AiPricing(Options.Create(options));
        pricing.Estimate("MY-MODEL", 1_000_000, 0).ShouldBe(1m);
        pricing.Estimate("gemini-3.8-flash", 1_000_000, 0).ShouldBe(0.75m);
    }

    public static TheoryData<string, string, string> BadConfig => new()
    {
        { "unknown provider", "AI:Provider", "llama" },
        { "bad model id", "AI:Gemini:Model", "gemini 3" },
        { "zero timeout", "AI:Interpret:TimeoutMs", "0" },
        { "bad thinking level", "AI:Gemini:ThinkingLevel", "none" },
        { "anthropic effort xhigh with thinking disabled", "AI:Anthropic:Effort", "xhigh" },
        { "negative budget", "AI:Budget:DailyCostCapUsd", "-1" },
        { "tiny token floor", "AI:OpenAI:MaxOutputTokens:Interpret", "4" },
        { "temperature out of range", "AI:OpenAI:Temperature", "3" },
    };

    [Theory]
    [MemberData(nameof(BadConfig))]
    public void Bad_configuration_fails_validation_SY36(string because, string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Should.Throw<OptionsValidationException>(() => Resolve(config).Value, because);
    }

    private static IOptions<AiOptions> Resolve(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddAi(config);
        return services.BuildServiceProvider().GetRequiredService<IOptions<AiOptions>>();
    }
}
