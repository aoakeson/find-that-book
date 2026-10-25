using System.Net;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;

namespace FindThatBook.Api.Tests.Http;

public sealed class HealthTests
{
    [Fact]
    public async Task Health_ReportsConfiguration_WithoutCallingOpenLibraryOrTheLlm()
    {
        await using var api = new ApiFactory();
        api.AiAdmin.SetKey("gemini", "test-key-12345678");
        var response = await api.Client().GetAsync("/api/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = await response.Read<HealthResponse>();
        health.Status.ShouldBe("ok");
        health.Environment.ShouldBe("Production");
        health.Version.ShouldNotBeNullOrWhiteSpace();
        health.AiProvider.ShouldBe("gemini");
        health.AiConfigured.ShouldBe(true);
        health.JudgeEnabled.ShouldBe(false);
        health.SuggestIndex.ShouldBe(new SuggestIndexInfo { Version = "test-1", Count = 3 });
        api.Catalog.Calls.ShouldBe(0);
        api.Interpreter.Calls.ShouldBe(0);
        response.Headers.GetValues("X-Request-Id").Single().ShouldNotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Health_SY34_ReportsUpstreamStatus()
    {
        await using var api = new ApiFactory();
        var response = await api.Client().GetAsync("/api/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = await response.Read<HealthResponse>();

        // Upstream health is present and starts with default values (no failures recorded)
        health.Upstream.ShouldNotBeNull();
        health.Upstream.OpenLibraryCircuit.ShouldBe("closed");
        health.Upstream.OpenLibraryLastFailure.ShouldBeNull();
        health.Upstream.OpenLibraryLastFailureReason.ShouldBeNull();
        // LLM status may be null if not configured, or "ok" otherwise
        health.Upstream.LlmLastFailure.ShouldBeNull();
        health.Upstream.LlmLastFailureReason.ShouldBeNull();
    }

    [Fact]
    public async Task Health_StillAnswers_WhenAComponentThrows()
    {
        await using var api = new ApiFactory
        {
            ExtraServices = s => s.AddSingleton<FindThatBook.Infrastructure.Ai.IAiAdmin>(new ThrowingAdmin()),
        };
        var response = await api.Client().GetAsync("/api/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = await response.Read<HealthResponse>();
        health.AiProvider.ShouldBeNull();
        health.AiConfigured.ShouldBeNull();
        api.Logs.Named("health.component_failed").ShouldHaveSingleItem();
    }

    private sealed class ThrowingAdmin : FindThatBook.Infrastructure.Ai.IAiAdmin
    {
        public FindThatBook.Infrastructure.Ai.AiStatus GetStatus() => throw new InvalidOperationException("broken");
        public FindThatBook.Infrastructure.Ai.AiStatus Update(FindThatBook.Infrastructure.Ai.AiSettingsUpdate update) => throw new NotSupportedException();
        public FindThatBook.Infrastructure.Ai.AiStatus SetKey(string provider, string apiKey) => throw new NotSupportedException();
        public FindThatBook.Infrastructure.Ai.AiStatus ClearKey(string provider) => throw new NotSupportedException();
        public Task<FindThatBook.Infrastructure.Ai.AiProbeResult> ProbeAsync(string provider, CancellationToken ct) => throw new NotSupportedException();
    }
}
