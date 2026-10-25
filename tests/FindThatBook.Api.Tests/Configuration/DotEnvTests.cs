using FindThatBook.Infrastructure.Configuration;

namespace FindThatBook.Api.Tests.Configuration;

/// <summary>The .env reader and the provider-SDK names. All values here are fake.</summary>
public sealed class DotEnvTests
{
    public static TheoryData<string, string, string, string> Lines => new()
    {
        // case, file text, key, expected value
        { "plain", "A=1", "A", "1" },
        { "spaces around =", "A = 1  ", "A", "1" },
        { "export prefix", "export A=1", "A", "1" },
        { "double quotes", "A=\"a b\"", "A", "a b" },
        { "single quotes are literal", "A='a\\nb # c'", "A", "a\\nb # c" },
        { "double-quote escapes", "A=\"x\\ny\\t\\\"z\\\"\"", "A", "x\ny\t\"z\"" },
        { "inline comment after a value", "A=value # note", "A", "value" },
        { "hash inside a value", "A=abc#def", "A", "abc#def" },
        { "= inside a value", "A=b=c", "A", "b=c" },
        { "comment after a quoted value", "A=\"v\" # note", "A", "v" },
        { "CRLF line endings", "A=1\r\nB=2\r\n", "B", "2" },
        { "BOM", "﻿A=1", "A", "1" },
        { "multi-line double-quoted", "A=\"line1\nline2\"\nB=3", "A", "line1\nline2" },
        { "hierarchical name", "AI__Gemini__Model=m", "AI__Gemini__Model", "m" },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void Parse_ReadsCommonDotEnvSyntax(string caseId, string text, string key, string expected)
    {
        var entries = DotEnvFile.Parse(text);
        entries.Last(e => e.Key == key).Value.ShouldBe(expected, caseId);
    }

    [Theory]
    [InlineData("comment line", "# A=1")]
    [InlineData("blank lines", "\n\n   \n")]
    [InlineData("no equals sign", "JUSTAWORD")]
    [InlineData("empty key", "=value")]
    [InlineData("key with a space", "MY KEY=1")]
    [InlineData("key starting with a digit", "1A=1")]
    [InlineData("unterminated quote", "A=\"never closed")]
    public void Parse_SkipsLinesItCannotRead(string caseId, string text) => DotEnvFile.Parse(text).ShouldBeEmpty(caseId);

    [Fact]
    public void Map_TranslatesProviderNames_AndDoubleUnderscores()
    {
        var data = WellKnownEnvironmentNames.Map(
        [
            new("GEMINI_API_KEY", "g"), new("ANTHROPIC_API_KEY", "a"), new("OPENAI_API_KEY", "o"),
            new("AI__Provider", "anthropic"), new("Judge__Enabled", "true"),
        ]);
        data["AI:Gemini:ApiKey"].ShouldBe("g");
        data["AI:Anthropic:ApiKey"].ShouldBe("a");
        data["AI:OpenAI:ApiKey"].ShouldBe("o");
        data["AI:Provider"].ShouldBe("anthropic");
        data["Judge:Enabled"].ShouldBe("true");
        data.ShouldNotContainKey("GEMINI_API_KEY");
    }

    [Fact]
    public void Map_GeminiBeatsGoogle_AndTheExplicitNameBeatsBoth_WhateverTheOrder()
    {
        WellKnownEnvironmentNames.Map([new("GEMINI_API_KEY", "gemini"), new("GOOGLE_API_KEY", "google")])["AI:Gemini:ApiKey"].ShouldBe("gemini");
        WellKnownEnvironmentNames.Map([new("AI__Gemini__ApiKey", "explicit"), new("GEMINI_API_KEY", "alias")])["AI:Gemini:ApiKey"].ShouldBe("explicit");
    }

    [Fact]
    public void Map_IgnoresEmptyValues_SoACopiedExampleFileChangesNothing()
    {
        var data = WellKnownEnvironmentNames.Map([new("GEMINI_API_KEY", ""), new("AI__Provider", "  "), new("GOOGLE_API_KEY", "google")]);
        data["AI:Gemini:ApiKey"].ShouldBe("google");
        data.ShouldNotContainKey("AI:Provider");
    }

    [Fact]
    public void Provider_NeverPrintsValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ftb-{Guid.NewGuid():N}.env");
        File.WriteAllText(path, "GEMINI_API_KEY=fake-secret-value");
        try
        {
            var provider = new DotEnvConfigurationProvider(path);
            provider.Load();
            provider.TryGet("AI:Gemini:ApiKey", out var value).ShouldBeTrue();
            value.ShouldBe("fake-secret-value");
            provider.ToString().ShouldNotContain("fake-secret");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Provider_WithoutAFile_IsEmpty()
    {
        var provider = new DotEnvConfigurationProvider(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.env"));
        provider.Load();
        provider.GetChildKeys([], null).ShouldBeEmpty();
    }
}
