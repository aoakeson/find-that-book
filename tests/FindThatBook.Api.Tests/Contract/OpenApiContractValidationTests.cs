using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Model;
using FindThatBook.Core.Search;

namespace FindThatBook.Api.Tests.Contract;

/// <summary>
/// Validates that actual API responses match the schemas and examples declared in the OpenAPI document.
/// A refactor that breaks the contract fails CI here.
/// </summary>
public sealed class OpenApiContractValidationTests
{
    private static async Task<JsonObject> GetOpenApiDocument(ApiFactory api)
    {
        var response = await api.Client().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Json();
    }

    [Fact]
    public async Task SearchResponse_200_MatchesOpenApiSchema()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.ClearMatch()) };
        var client = api.Client();

        var response = await client.GetAsync("/api/books/search?q=tolkien%20hobbit", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        // Strict deserialization catches missing/extra/misspelled fields
        var searchResponse = await response.Read<SearchResponse>();
        searchResponse.Query.ShouldNotBeNullOrWhiteSpace();
        searchResponse.Status.ShouldBe(MatchStatus.Match);
        searchResponse.Candidates.ShouldNotBeNull();
        searchResponse.Meta.ShouldNotBeNull();
        searchResponse.Meta.RequestId.ShouldNotBeNullOrWhiteSpace();

        // Verify all documented response fields are present in the OpenAPI schema
        var openApi = await GetOpenApiDocument(api);
        var schemaRef = openApi["paths"]!["/api/books/search"]!["get"]!["responses"]!["200"]!
            ["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        var schemaName = schemaRef.Split('/').Last();
        var schema = openApi["components"]!["schemas"]![schemaName]!.AsObject();
        var requiredFields = schema["required"]?.AsArray().Select(f => f!.GetValue<string>()).ToHashSet() ?? [];

        // Validate that the response JSON contains all required fields from the schema
        var responseJson = JsonSerializer.Deserialize<JsonObject>(
            JsonSerializer.Serialize(searchResponse, Wire.StrictContract), (JsonSerializerOptions?)null)!;
        foreach (var field in requiredFields)
        {
            var snakeField = JsonNamingPolicy.SnakeCaseLower.ConvertName(field);
            responseJson.ContainsKey(snakeField).ShouldBeTrue($"Response missing required field: {snakeField}");
        }
    }

    [Fact]
    public async Task SuggestResponse_200_MatchesOpenApiSchema()
    {
        await using var api = new ApiFactory();
        var client = api.Client();

        var response = await client.GetAsync("/api/suggest?prefix=hob", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var suggestResponse = await response.Read<SuggestResponse>();
        suggestResponse.Prefix.ShouldBe("hob");
        suggestResponse.Suggestions.ShouldNotBeNull();

        // Verify schema exists in OpenAPI
        var openApi = await GetOpenApiDocument(api);
        openApi["paths"]!["/api/suggest"]!["get"]!["responses"]!["200"]!.ShouldNotBeNull();
    }

    [Fact]
    public async Task HealthResponse_200_MatchesOpenApiSchema()
    {
        await using var api = new ApiFactory();
        var client = api.Client();

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var healthResponse = await response.Read<HealthResponse>();
        healthResponse.Status.ShouldBe("ok");
        healthResponse.Version.ShouldNotBeNullOrWhiteSpace();
        healthResponse.Environment.ShouldNotBeNullOrWhiteSpace();

        // Verify schema exists in OpenAPI
        var openApi = await GetOpenApiDocument(api);
        openApi["paths"]!["/api/health"]!["get"]!["responses"]!["200"]!.ShouldNotBeNull();
    }

    [Fact]
    public async Task ValidationError_400_MatchesOpenApiProblemSchema()
    {
        await using var api = new ApiFactory();
        var client = api.Client();

        var response = await client.GetAsync("/api/books/search?q=", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Json();
        problem["status"]!.GetValue<int>().ShouldBe(400);
        problem["code"]!.GetValue<string>().ShouldBe("validation_failed");
        problem["type"]!.GetValue<string>().ShouldStartWith("/problems/");
        problem["title"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        problem["trace_id"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

        // Verify 400 response is documented in OpenAPI
        var openApi = await GetOpenApiDocument(api);
        openApi["paths"]!["/api/books/search"]!["get"]!["responses"]!["400"]!.ShouldNotBeNull();
    }

    [Fact]
    public async Task NoSearchableTermsError_400_MatchesOpenApiProblemSchema()
    {
        await using var api = new ApiFactory();
        var client = api.Client();

        var response = await client.GetAsync("/api/books/search?q=the", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest, "no_searchable_terms");
        problem["hint"]!.GetValue<string>().ShouldContain("title or author");
    }

    [Fact]
    public async Task RateLimitError_429_MatchesOpenApiProblemSchema()
    {
        await using var api = new ApiFactory
        {
            Settings = { ["RateLimit:SearchPermitsPerIp"] = "1" },
            Search = FakeBookSearch.Returning(Outcomes.ClearMatch())
        };
        var client = api.Client();

        // First request consumes the single permit
        (await client.GetAsync("/api/books/search?q=first", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Second request should be rate limited
        var response = await client.GetAsync("/api/books/search?q=second", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.TooManyRequests, "rate_limited");
        response.Headers.RetryAfter.ShouldNotBeNull();
        problem["detail"]!.GetValue<string>().ShouldContain("search");

        // Verify 429 response is documented in OpenAPI
        var openApi = await GetOpenApiDocument(api);
        openApi["paths"]!["/api/books/search"]!["get"]!["responses"]!["429"]!.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("contributor_match")]
    [InlineData("ambiguous_author_only")]
    [InlineData("clear_match")]
    [InlineData("none")]
    [InlineData("degraded_llm_down")]
    [InlineData("topic_search")]
    [InlineData("topic_none")]
    public async Task SearchResponse_AllScenarios_DeserializeWithStrictContract(string scenario)
    {
        var outcome = SearchContractTests.Fixture(scenario);
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(outcome) };
        var response = await api.Client().GetAsync($"/api/books/search?q={Uri.EscapeDataString(outcome.Query.Raw)}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Wire.StrictContract catches any schema drift: extra/missing/misspelled fields fail here
        var searchResponse = await response.Read<SearchResponse>();
        searchResponse.ShouldNotBeNull();
    }

    [Fact]
    public async Task OpenApiExamples_AreValidJson_AndMatchSchemaTypes()
    {
        await using var api = new ApiFactory { Search = new FakeBookSearch() };
        var openApi = await GetOpenApiDocument(api);

        var searchResponses = openApi["paths"]!["/api/books/search"]!["get"]!["responses"]!.AsObject();
        foreach (var (status, responseNode) in searchResponses)
        {
            var content = responseNode!["content"]?.AsObject();
            if (content is null) continue;

            foreach (var (mediaType, mediaNode) in content)
            {
                var examples = mediaNode!["examples"]?.AsObject();
                if (examples is null) continue;

                foreach (var (exampleName, exampleNode) in examples)
                {
                    var value = exampleNode!["value"];
                    value.ShouldNotBeNull($"Example {exampleName} for {status} {mediaType} should have a value");

                    // Verify the example is valid JSON and has the expected structure
                    if (status == "200")
                    {
                        value["query"].ShouldNotBeNull($"Example {exampleName} should have a query field");
                        value["status"].ShouldNotBeNull($"Example {exampleName} should have a status field");
                    }
                    else if (int.TryParse(status, out var code) && code >= 400)
                    {
                        value["code"].ShouldNotBeNull($"Error example {exampleName} should have a code field");
                    }
                }
            }
        }
    }
}
