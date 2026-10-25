using System.Text.Json.Nodes;
using FindThatBook.Api.Tests.Support;

namespace FindThatBook.Api.Tests.Contract;

/// <summary>/openapi/v1.json is part of the deliverable: paths, parameter rules, string enums and named examples.</summary>
public sealed class OpenApiDocumentTests
{
    private static async Task<JsonObject> Document(string environment = "Production")
    {
        await using var api = new ApiFactory { EnvironmentName = environment, Search = new FakeBookSearch() };
        var response = await api.Client().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Json();
    }

    [Fact]
    public async Task Document_DescribesTheApi()
    {
        var doc = await Document();

        doc["openapi"]!.GetValue<string>().ShouldStartWith("3.1");
        doc["info"]!["title"]!.GetValue<string>().ShouldBe("Find That Book API");
        doc["info"]!["description"]!.GetValue<string>().ShouldContain("AI interprets, code decides");
        var paths = doc["paths"]!.AsObject().Select(p => p.Key).ToList();
        paths.ShouldContain("/api/books/search");
        paths.ShouldContain("/api/suggest");
        paths.ShouldContain("/api/health");
        paths.ShouldNotContain(p => p.StartsWith("/api/admin", StringComparison.Ordinal));
        paths.ShouldNotContain(p => p.Contains("rest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchOperation_StatesTheRealParameterRules()
    {
        var op = (await Document())["paths"]!["/api/books/search"]!["get"]!;
        op["operationId"]!.GetValue<string>().ShouldBe("SearchBooks");
        op["summary"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        op["description"]!.GetValue<string>().ShouldContain("match_tier");

        var parameters = op["parameters"]!.AsArray().ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!);
        parameters["q"]["required"]!.GetValue<bool>().ShouldBeTrue();
        parameters["q"]["schema"]!["maxLength"]!.GetValue<int>().ShouldBe(300);
        var limit = parameters["limit"]["schema"]!;
        limit["type"]!.ToString().ShouldContain("integer");
        limit["minimum"]!.ToString().ShouldBe("1");
        limit["maximum"]!.ToString().ShouldBe("10");
        limit["default"]!.GetValue<int>().ShouldBe(5);

        op["responses"]!.AsObject().Select(r => r.Key).ShouldBe(["200", "400", "429", "502", "503", "504"], ignoreOrder: true);
    }

    [Fact]
    public async Task NamedExamples_AreAttachedToTheirResponses()
    {
        var responses = (await Document())["paths"]!["/api/books/search"]!["get"]!["responses"]!;
        string[] Names(string status) => [.. responses[status]!["content"]!.AsObject().Single().Value!["examples"]!.AsObject().Select(e => e.Key)];

        Names("200").ShouldBe(["ambiguous_author_only", "clear_match", "contributor_match", "degraded_llm_down", "none", "topic_none", "topic_search"], ignoreOrder: true);
        Names("400").ShouldBe(["no_searchable_terms", "validation_failed"], ignoreOrder: true);
        Names("429").ShouldBe(["rate_limited"]);
        Names("503").ShouldBe(["catalog_unavailable"]);
        Names("504").ShouldBe(["catalog_timeout", "search_timeout"], ignoreOrder: true);
        var clear = responses["200"]!["content"]!["application/json"]!["examples"]!["clear_match"]!;
        clear["value"]!["candidates"]![0]!["work_id"]!.GetValue<string>().ShouldBe("OL27482W");
    }

    [Fact]
    public async Task Enums_AreSnakeCaseStrings()
    {
        var text = (await Document()).ToJsonString();
        foreach (var value in new[] { "\"ambiguous\"", "\"exact_title_contributor_author\"", "\"judge_low_confidence\"", "\"interpretation_fallback\"", "\"chose_none\"", "\"heuristic\"", "\"template\"" })
            text.ShouldContain(value);
        var schemas = (await Document())["components"]!["schemas"]!.AsObject();
        var tier = schemas["MatchTier"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>());
        tier.ShouldBe(["a", "b", "c", "d", "e"]);
    }

    [Fact]
    public async Task ContractXmlComments_BecomeSchemaDescriptions()
    {
        var schemas = (await Document())["components"]!["schemas"]!;
        schemas["SearchResponse"]!["description"]!.GetValue<string>().ShouldContain("200 for match, ambiguous and none");
        schemas["SearchResponse"]!["properties"]!["query"]!["description"]!.GetValue<string>().ShouldContain("trimmed");
        schemas["SearchCandidate"]!["properties"]!["author"]!["description"]!.GetValue<string>().ShouldContain("Primary author");
        schemas["LlmMeta"]!["properties"]!["est_cost_usd"]!["description"]!.GetValue<string>().ShouldContain("null");
    }

    [Fact]
    public async Task AdminEndpoints_AreNeverDocumented_EvenInDevelopment()
    {
        var doc = await Document("Development");
        doc["paths"]!.AsObject().Select(p => p.Key).ShouldNotContain(p => p.StartsWith("/api/admin", StringComparison.Ordinal));
        doc["tags"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ShouldNotContain("Admin");
    }
}
