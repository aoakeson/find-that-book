using System.Net;
using FindThatBook.Api.Tests.Support;

namespace FindThatBook.Api.Tests.Http;

/// <summary>SY-16..SY-18: everything under /api answers JSON, including mistakes; docs are reachable in Production.</summary>
public sealed class RoutingTests(FakeSearchApi fixture) : IClassFixture<FakeSearchApi>
{
    private readonly HttpClient _client = fixture.Reset().Client;

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api/books")]
    [InlineData("/api/books/search/extra")]
    [InlineData("/api/admin/ai")] // not mapped outside Development
    public async Task SY16_UnknownApiRoute_Is404ProblemJson(string path)
    {
        var problem = await (await _client.GetAsync(path, TestContext.Current.CancellationToken))
            .ShouldBeProblem(HttpStatusCode.NotFound, "not_found");
        problem["detail"]!.GetValue<string>().ShouldContain("/scalar");
    }

    [Theory]
    [InlineData("POST", "/api/books/search")]
    [InlineData("HEAD", "/api/books/search")]
    [InlineData("PUT", "/api/health")]
    [InlineData("DELETE", "/api/suggest")]
    public async Task SY17_WrongMethodOnAKnownRoute_Is405_WithAllow(string method, string path)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldBe(["GET"]);
        if (method != "HEAD") await response.ShouldBeProblem(HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Fact]
    public async Task SY18_TrailingSlash_StillSearches()
    {
        var response = await _client.GetAsync("/api/books/search/?q=hobbit", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_IsServedInProduction()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Json())["openapi"]!.GetValue<string>().ShouldStartWith("3.1");
    }

    [Fact]
    public async Task Scalar_IsServedInProduction()
    {
        var response = await _client.GetAsync("/scalar", TestContext.Current.CancellationToken);
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently or HttpStatusCode.Found)
            response = await _client.GetAsync(response.Headers.Location!, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Find That Book API");
    }
}
