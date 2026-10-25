using System.Text.Json;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace FindThatBook.Infrastructure.Tests.Ai;

public sealed class AiAdminTests
{
    private const string Good = """{"title":"The Hobbit","author":"J.R.R. Tolkien","characters":[],"keywords":[],"english_title":"The Hobbit","original_title":null}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Status_never_contains_a_key_only_source_and_fingerprint()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        host.Admin.SetKey("anthropic", "test-runtime-anthropic-key-3333");
        var status = host.Admin.GetStatus();

        var json = JsonSerializer.Serialize(status);
        foreach (var secret in new[] { AiTestHost.GeminiKey, AiTestHost.OpenAiKey, "test-runtime-anthropic-key-3333" })
            json.ShouldNotContain(secret);

        status.ActiveProvider.ShouldBe("gemini");
        status.AiConfigured.ShouldBeTrue();
        status.Persistence.ShouldContain("lost on restart");
        var anthropic = status.Providers.Single(p => p.Name == "anthropic");
        anthropic.KeySource.ShouldBe(KeySources.Runtime);
        anthropic.KeyFingerprint.ShouldBe(KeySources.Fingerprint("test-runtime-anthropic-key-3333"));
        status.Providers.Single(p => p.Name == "gemini").Active.ShouldBeTrue();
    }

    [Fact]
    public void Fingerprint_is_stable_short_and_distinct()
    {
        var a = KeySources.Fingerprint("key-one");
        a.ShouldBe(KeySources.Fingerprint("key-one"));
        a.ShouldMatch("^sha256:[0-9a-f]{8}$");
        a.ShouldNotBe(KeySources.Fingerprint("key-two"));
        new ResolvedKey("key-one", KeySources.Runtime).ToString().ShouldNotContain("key-one");
        new ProviderTarget("gemini", "m", new ResolvedKey("key-one", KeySources.Runtime), new GeminiOptions()).ToString().ShouldNotContain("key-one");
    }

    [Fact]
    public async Task Runtime_key_wins_and_clear_restores_the_configured_key()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        var configured = host.Settings.KeyFor("gemini");
        configured.Source.ShouldBe(KeySources.AppSettings); // in-memory test configuration

        host.Admin.SetKey("GEMINI", "runtime-key-5555").Providers.Single(p => p.Name == "gemini").KeySource.ShouldBe(KeySources.Runtime);
        host.Settings.KeyFor("gemini").Value.ShouldBe("runtime-key-5555");

        var cleared = host.Admin.ClearKey("gemini").Providers.Single(p => p.Name == "gemini");
        cleared.KeySource.ShouldBe(KeySources.AppSettings);
        cleared.KeyFingerprint.ShouldBe(configured.Fingerprint);
    }

    [Fact]
    public async Task Clearing_a_key_that_only_existed_at_runtime_leaves_none()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new() { ["AI:Anthropic:ApiKey"] = null });
        host.Admin.SetKey("anthropic", "only-runtime-7777");
        var status = host.Admin.ClearKey("anthropic").Providers.Single(p => p.Name == "anthropic");
        status.Configured.ShouldBeFalse();
        status.KeySource.ShouldBe(KeySources.None);
        status.KeyFingerprint.ShouldBeNull();
    }

    [Fact]
    public async Task Update_switches_provider_model_and_judge_in_memory()
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client);
        var status = host.Admin.Update(new AiSettingsUpdate("Anthropic", new Dictionary<string, string> { ["anthropic"] = "claude-sonnet-5-5" }, JudgeEnabled: true));

        status.ActiveProvider.ShouldBe("anthropic");
        status.Providers.Single(p => p.Name == "anthropic").Model.ShouldBe("claude-sonnet-5-5");
        status.Judge.Enabled.ShouldBeTrue();
        status.Judge.Configured.ShouldBeTrue(); // falls back to the OpenAI key
        status.Judge.KeyFingerprint.ShouldBe(KeySources.Fingerprint(AiTestHost.OpenAiKey));
        status.Judge.Threshold.ShouldBe(0.9);

        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Provider.ShouldBe("anthropic");
        result.Call.Model.ShouldBe("claude-sonnet-5-5");
    }

    [Fact]
    public async Task Provider_none_means_not_configured()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        host.Admin.Update(new AiSettingsUpdate(Provider: "none")).AiConfigured.ShouldBeFalse();
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
    }

    [Fact]
    public void Auto_is_the_default_setting() => new AiOptions().Provider.ShouldBe(AiProviders.Auto);

    [Theory]
    [InlineData("g", "a", "o", "gemini")]   // Gemini first (the brief's default)
    [InlineData(null, "a", "o", "anthropic")]
    [InlineData(null, null, "o", "openai")]
    [InlineData(null, null, null, "none")]
    public async Task Auto_picks_the_first_provider_that_has_a_key(string? gemini, string? anthropic, string? openai, string expected)
    {
        var client = FakeChatClient.Json(Good);
        await using var host = new AiTestHost(client, new()
        {
            ["AI:Provider"] = "auto",
            ["AI:Gemini:ApiKey"] = gemini, ["AI:Anthropic:ApiKey"] = anthropic, ["AI:OpenAI:ApiKey"] = openai,
        });

        var status = host.Admin.GetStatus();
        status.ActiveProvider.ShouldBe(expected);
        status.ProviderSelection.ShouldBe("auto");
        status.AiConfigured.ShouldBe(expected != "none");
        status.Providers.Where(p => p.Active).Select(p => p.Name).ShouldBe(expected == "none" ? [] : [expected]);

        var result = await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct);
        result.Call.Provider.ShouldBe(expected);
        if (expected == "none")
        {
            result.Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
            result.Call.Detail.ShouldBe("no_api_key");
        }
        else
        {
            result.Call.Status.ShouldBe(LlmCallStatus.Ok);
        }
    }

    [Fact]
    public async Task Explicit_provider_is_respected_even_without_a_key()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new() { ["AI:Provider"] = "openai", ["AI:OpenAI:ApiKey"] = null });
        var status = host.Admin.GetStatus();
        (status.ActiveProvider, status.ProviderSelection, status.AiConfigured).ShouldBe(("openai", "explicit", false));
        (await host.Llm.CallAsync<TestOutput>(AiTestHost.Call(), Ct)).Call.Status.ShouldBe(LlmCallStatus.NotConfigured);
    }

    [Fact]
    public async Task Runtime_keys_feed_auto_selection_in_priority_order()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new()
        {
            ["AI:Provider"] = "auto", ["AI:Gemini:ApiKey"] = null, ["AI:Anthropic:ApiKey"] = null, ["AI:OpenAI:ApiKey"] = null,
        });
        host.Admin.GetStatus().ActiveProvider.ShouldBe("none");
        host.Admin.SetKey("openai", "runtime-openai-1").ActiveProvider.ShouldBe("openai");
        host.Admin.SetKey("gemini", "runtime-gemini-1").ActiveProvider.ShouldBe("gemini"); // higher priority wins
        host.Admin.ClearKey("gemini").ActiveProvider.ShouldBe("openai");
        host.Admin.SetKey("judge", "judge-only").ActiveProvider.ShouldBe("openai"); // a judge key is not a chat key
    }

    [Fact]
    public async Task Auto_skips_a_disabled_provider_until_its_key_is_saved_again()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new() { ["AI:Provider"] = "auto" });
        var target = host.Settings.Target("gemini")!;
        host.Get<ProviderHealth>().Disable(target, "unauthorized");
        host.Settings.ActiveProvider.ShouldBe("anthropic");

        host.Settings.SetKey("gemini", AiTestHost.GeminiKey);
        host.Settings.ActiveProvider.ShouldBe("gemini");
        host.Get<ProviderHealth>().DisabledReason(host.Settings.Target("gemini")!).ShouldBeNull();
    }

    [Fact]
    public async Task Admin_can_switch_to_an_explicit_provider_and_back_to_auto()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new() { ["AI:Provider"] = "auto" });
        var explicitStatus = host.Admin.Update(new AiSettingsUpdate(Provider: "anthropic"));
        (explicitStatus.ActiveProvider, explicitStatus.ProviderSelection).ShouldBe(("anthropic", "explicit"));
        var auto = host.Admin.Update(new AiSettingsUpdate(Provider: "AUTO"));
        (auto.ActiveProvider, auto.ProviderSelection).ShouldBe(("gemini", "auto"));
        await Should.ThrowAsync<ArgumentException>(() => host.Admin.ProbeAsync("auto", Ct)); // a probe needs a real provider
        Should.Throw<ArgumentException>(() => host.Admin.SetKey("auto", "k"));
    }

    public static TheoryData<string, AiSettingsUpdate> BadUpdates => new()
    {
        { "model for auto", new AiSettingsUpdate(Models: new Dictionary<string, string> { ["auto"] = "m" }) },
        { "unknown provider", new AiSettingsUpdate(Provider: "llama") },
        { "model for unknown provider", new AiSettingsUpdate(Models: new Dictionary<string, string> { ["mistral"] = "m" }) },
        { "model for none", new AiSettingsUpdate(Models: new Dictionary<string, string> { ["none"] = "m" }) },
        { "blank model", new AiSettingsUpdate(Models: new Dictionary<string, string> { ["gemini"] = " " }) },
        { "model with spaces", new AiSettingsUpdate(Models: new Dictionary<string, string> { ["gemini"] = "gemini 3" }) },
    };

    [Theory]
    [MemberData(nameof(BadUpdates))]
    public async Task Invalid_update_throws_and_changes_nothing(string because, AiSettingsUpdate update)
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        var before = JsonSerializer.Serialize(host.Admin.GetStatus());
        Should.Throw<ArgumentException>(() => host.Admin.Update(update with { JudgeEnabled = true }), because);
        JsonSerializer.Serialize(host.Admin.GetStatus()).ShouldBe(before, because);
    }

    [Theory]
    [InlineData("llama", "key")]
    [InlineData("gemini", "")]
    [InlineData("gemini", "has space")]
    public async Task Invalid_key_input_throws(string provider, string key)
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        Should.Throw<ArgumentException>(() => host.Admin.SetKey(provider, key));
    }

    [Fact]
    public async Task Judge_has_its_own_runtime_key()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good));
        var judge = host.Admin.SetKey("judge", "judge-only-8888").Judge;
        judge.KeySource.ShouldBe(KeySources.Runtime);
        judge.KeyFingerprint.ShouldBe(KeySources.Fingerprint("judge-only-8888"));
        host.Admin.ClearKey("judge").Judge.KeyFingerprint.ShouldBe(KeySources.Fingerprint(AiTestHost.OpenAiKey));
    }

    [Fact]
    public async Task Probe_makes_one_real_uncached_interpret_call()
    {
        var client = FakeChatClient.Json(Good, input: 300, output: 40);
        await using var host = new AiTestHost(client);
        var first = await host.Admin.ProbeAsync("gemini", Ct);
        var second = await host.Admin.ProbeAsync("gemini", Ct);

        first.Ok.ShouldBeTrue();
        first.Status.ShouldBe("ok");
        first.Model.ShouldBe("gemini-3.5-flash-lite");
        first.InputTokens.ShouldBe(300);
        first.Detail.ShouldBe("title=The Hobbit; author=J.R.R. Tolkien");
        second.InputTokens.ShouldBe(300);
        client.Calls.ShouldBe(2);
        client.LastMessages![1].Text.ShouldBe("\"tolkien hobbit\"");
    }

    [Fact]
    public async Task Probe_of_a_provider_without_key_reports_not_configured()
    {
        await using var host = new AiTestHost(FakeChatClient.Json(Good), new() { ["AI:OpenAI:ApiKey"] = null });
        var probe = await host.Admin.ProbeAsync("openai", Ct);
        probe.Ok.ShouldBeFalse();
        probe.Status.ShouldBe("not_configured");
        await Should.ThrowAsync<ArgumentException>(() => host.Admin.ProbeAsync("bard", Ct));
    }

    /// <summary>The API's real layers: appsettings &lt; user secrets &lt; .env &lt; well-known names &lt; environment.</summary>
    [Fact]
    public void Key_source_comes_from_the_configuration_layer_that_won()
    {
        Environment.SetEnvironmentVariable("FTB_TEST_A5__AI__Gemini__ApiKey", "from-env-1");
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "appsettings.json"), """{"AI":{"Gemini":{"ApiKey":"from-json"},"OpenAI":{"ApiKey":"from-json-openai"},"Extra":{"ApiKey":"json-only"}}}""");
            File.WriteAllText(Path.Combine(dir.FullName, "secrets.json"), """{"AI":{"Anthropic":{"ApiKey":"from-secrets"}}}""");
            File.WriteAllText(Path.Combine(dir.FullName, ".env"), "OPENAI_API_KEY=from-dotenv\n");
            var config = new ConfigurationBuilder()
                .SetBasePath(dir.FullName)
                .AddJsonFile("appsettings.json")
                .AddJsonFile("secrets.json")
                .Add(new DotEnvConfigurationSource { Path = Path.Combine(dir.FullName, ".env") })
                .Add(new EnvironmentAliasConfigurationSource { Variables = new Dictionary<string, string> { ["GEMINI_API_KEY"] = "from-alias" } })
                .AddEnvironmentVariables("FTB_TEST_A5__")
                .Build();

            KeySources.Find(config, "AI:Gemini:ApiKey").Source.ShouldBe(KeySources.Environment);
            KeySources.Find(config, "AI:Gemini:ApiKey").Value.ShouldBe("from-env-1");
            KeySources.Find(config, "AI:Anthropic:ApiKey").Source.ShouldBe(KeySources.UserSecrets);
            KeySources.Find(config, "AI:OpenAI:ApiKey").Source.ShouldBe(KeySources.DotEnv);
            KeySources.Find(config, "AI:OpenAI:ApiKey").Value.ShouldBe("from-dotenv");
            KeySources.Find(config, "AI:Extra:ApiKey").Source.ShouldBe(KeySources.AppSettings);
            KeySources.Find(config, "AI:Missing:ApiKey").Source.ShouldBe(KeySources.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FTB_TEST_A5__AI__Gemini__ApiKey", null);
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Well_known_environment_name_counts_as_environment()
    {
        var config = new ConfigurationBuilder()
            .Add(new EnvironmentAliasConfigurationSource { Variables = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "alias-key" } })
            .Build();
        var key = KeySources.Find(config, "AI:Anthropic:ApiKey");
        key.Source.ShouldBe(KeySources.Environment);
        key.Fingerprint.ShouldBe(KeySources.Fingerprint("alias-key"));
    }
}
