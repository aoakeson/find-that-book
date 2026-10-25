using FindThatBook.Api.Tests.Support;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace FindThatBook.Api.Tests.Configuration;

/// <summary>
/// appsettings &lt; appsettings.{Env} &lt; user secrets &lt; .env &lt; GEMINI_API_KEY-style names &lt; AI__… environment
/// variables &lt; command line. Tests use their own key names (FTB_TEST…) or a fake environment, never the real keys,
/// so a failing assertion can't print a developer's secret.
/// </summary>
[Collection(nameof(ProcessEnvironment))]
public sealed class PrecedenceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ftb-config-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string DotEnv(string text)
    {
        var path = Path.Combine(_dir, ".env");
        File.WriteAllText(path, text);
        return path;
    }

    private static MemoryConfigurationSource Layer(string name, params (string Key, string Value)[] values) =>
        new() { InitialData = values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)).Append(new($"Layer:{name}", "1")) };

    [Fact]
    public void Sources_AreInsertedJustBelowThePlainEnvironmentVariables()
    {
        var builder = new ConfigurationManager();
        builder.Sources.Clear();
        builder.Sources.Add(Layer("appsettings"));
        builder.AddEnvironmentVariables("DOTNET_");
        builder.Sources.Add(Layer("secrets"));
        builder.AddEnvironmentVariables();
        builder.AddCommandLine([]);

        FindThatBookConfiguration.InsertSources(builder.Sources, DotEnv(""), new EnvironmentAliasConfigurationSource());

        builder.Sources.Select(s => s.GetType()).ShouldBe(
        [
            typeof(MemoryConfigurationSource), typeof(EnvironmentVariablesConfigurationSource), typeof(MemoryConfigurationSource),
            typeof(DotEnvConfigurationSource), typeof(EnvironmentAliasConfigurationSource),
            typeof(EnvironmentVariablesConfigurationSource), typeof(CommandLineConfigurationSource),
        ]);
    }

    [Fact]
    public void EachLayer_OverridesTheOneBelow()
    {
        const string key = "FTB_TEST:Precedence:Value";
        Environment.SetEnvironmentVariable("FTB_TEST__Precedence__FromEnv", "env");
        Environment.SetEnvironmentVariable("FTB_TEST__Precedence__Value", "env");
        try
        {
            var builder = new ConfigurationManager();
            builder.Sources.Clear();
            builder.Sources.Add(Layer("appsettings", (key, "appsettings"), ("FTB_TEST:Precedence:OnlyAppSettings", "appsettings")));
            builder.Sources.Add(Layer("secrets", (key, "secrets"), ("FTB_TEST:Precedence:Secrets", "secrets")));
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(["--FTB_TEST:Precedence:FromEnv=cmd"]);
            FindThatBookConfiguration.InsertSources(builder.Sources,
                DotEnv("FTB_TEST__Precedence__Value=dotenv\nFTB_TEST__Precedence__Secrets=dotenv\n"), null);

            builder[key].ShouldBe("env");                                   // env beats .env
            builder["FTB_TEST:Precedence:Secrets"].ShouldBe("dotenv");       // .env beats user secrets
            builder["FTB_TEST:Precedence:OnlyAppSettings"].ShouldBe("appsettings");
            builder["FTB_TEST:Precedence:FromEnv"].ShouldBe("cmd");          // command line beats env
        }
        finally
        {
            Environment.SetEnvironmentVariable("FTB_TEST__Precedence__FromEnv", null);
            Environment.SetEnvironmentVariable("FTB_TEST__Precedence__Value", null);
        }
    }

    [Fact]
    public void ProviderNames_FromTheEnvironment_BeatDotEnv_AndLoseToExplicitNames()
    {
        var builder = new ConfigurationManager();
        builder.Sources.Clear();
        builder.Sources.Add(Layer("appsettings", ("AI:Anthropic:ApiKey", "appsettings-fake"), ("AI:OpenAI:ApiKey", "appsettings-fake")));
        builder.Sources.Add(Layer("explicit-env", ("AI:OpenAI:ApiKey", "explicit-fake"))); // stands in for AI__OpenAI__ApiKey
        builder.Sources.Insert(1, new EnvironmentAliasConfigurationSource
        {
            Variables = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "alias-fake", ["OPENAI_API_KEY"] = "alias-fake" },
        });
        builder.Sources.Insert(1, new DotEnvConfigurationSource { Path = DotEnv("ANTHROPIC_API_KEY=dotenv-fake\nGEMINI_API_KEY=dotenv-fake\n") });

        builder["AI:Gemini:ApiKey"].ShouldBe("dotenv-fake");
        builder["AI:Anthropic:ApiKey"].ShouldBe("alias-fake");
        builder["AI:OpenAI:ApiKey"].ShouldBe("explicit-fake");
        ConfigurationOrigin.Of(builder, "AI:Gemini:ApiKey").ShouldBe("dotenv");
        ConfigurationOrigin.Of(builder, "AI:Anthropic:ApiKey").ShouldBe("environment");
        ConfigurationOrigin.Of(builder, "AI:Missing:ApiKey").ShouldBe("none");
    }

    [Fact]
    public void ConfigurationOrigin_NamesEachKindOfLayer()
    {
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), """{"FTB_TEST":{"A":"x"}}""");
        File.WriteAllText(Path.Combine(_dir, "secrets.json"), """{"FTB_TEST":{"B":"x"}}""");
        var builder = new ConfigurationManager();
        builder.Sources.Clear();
        builder.SetBasePath(_dir).AddJsonFile("appsettings.json").AddJsonFile("secrets.json");
        builder.AddCommandLine(["--FTB_TEST:C=x"]);

        ConfigurationOrigin.Of(builder, "FTB_TEST:A").ShouldBe("appsettings");
        ConfigurationOrigin.Of(builder, "FTB_TEST:B").ShouldBe("user_secrets");
        ConfigurationOrigin.Of(builder, "FTB_TEST:C").ShouldBe("command_line");
    }

    [Fact]
    public void RepositoryRoot_IsFoundByWalkingUpToTheSolutionFile()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_dir, "src", "FindThatBook.Api", "bin")).FullName;
        File.WriteAllText(Path.Combine(_dir, FindThatBookConfiguration.SolutionFileName), "<Solution />");

        FindThatBookConfiguration.FindRepositoryRoot(null, nested).ShouldBe(new DirectoryInfo(_dir).FullName);
        FindThatBookConfiguration.FindDotEnvFile(nested).ShouldBe(Path.Combine(new DirectoryInfo(_dir).FullName, ".env"));
        FindThatBookConfiguration.FindRepositoryRoot(Path.GetTempPath()).ShouldNotBe(_dir);
    }

    [Fact]
    public void BuildForTool_ReadsTheApiAppSettings_DotEnv_AndArgs()
    {
        var api = Directory.CreateDirectory(Path.Combine(_dir, "src", "FindThatBook.Api")).FullName;
        File.WriteAllText(Path.Combine(_dir, FindThatBookConfiguration.SolutionFileName), "<Solution />");
        File.WriteAllText(Path.Combine(api, "appsettings.json"), """{"FTB_TEST":{"Base":"appsettings","Env":"appsettings"}}""");
        File.WriteAllText(Path.Combine(api, "appsettings.Staging.json"), """{"FTB_TEST":{"Env":"staging"}}""");
        File.WriteAllText(Path.Combine(_dir, ".env"), "FTB_TEST__FromDotEnv=dotenv\n");

        var config = FindThatBookConfiguration.BuildForTool(["--FTB_TEST:Arg=cmd"], "Staging", _dir);

        config["FTB_TEST:Base"].ShouldBe("appsettings");
        config["FTB_TEST:Env"].ShouldBe("staging");
        config["FTB_TEST:FromDotEnv"].ShouldBe("dotenv");
        config["FTB_TEST:Arg"].ShouldBe("cmd");
    }
}

/// <summary>Tests that set process environment variables run alone.</summary>
[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment;

/// <summary>The API finds the .env above its content root, and the off switch reaches Program.cs before it reads it.</summary>
public sealed class ApiDotEnvTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ftb-repo-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(true, "from-dotenv")]
    [InlineData(false, null)]
    public async Task TheApi_LoadsTheRepoRootDotEnv_UnlessSwitchedOff(bool enabled, string? expected)
    {
        File.WriteAllText(Path.Combine(_root, FindThatBookConfiguration.SolutionFileName), "<Solution />");
        File.WriteAllText(Path.Combine(_root, ".env"), "FTB_TEST__Probe=from-dotenv\n");
        var contentRoot = Directory.CreateDirectory(Path.Combine(_root, "src", "FindThatBook.Api")).FullName;
        await using var api = new ApiFactory
        {
            Search = new FakeBookSearch(),
            ContentRoot = contentRoot,
            Settings = { ["DotEnv:Enabled"] = enabled ? "true" : "false" },
        };
        _ = api.Server;
        api.Services.GetRequiredService<IConfiguration>()["FTB_TEST:Probe"].ShouldBe(expected);
    }
}
