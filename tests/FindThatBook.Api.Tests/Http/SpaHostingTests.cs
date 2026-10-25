using System.Net;
using FindThatBook.Api.Tests.Support;

namespace FindThatBook.Api.Tests.Http;

/// <summary>With a frontend build in wwwroot, client routes get index.html but /api, /scalar and /openapi never do (SY-16).</summary>
public sealed class SpaHostingTests : IAsyncLifetime
{
    private const string Marker = "<!-- find-that-book spa -->";
    private readonly string _webRoot = Directory.CreateTempSubdirectory("ftb-wwwroot-").FullName;
    private ApiFactory _api = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<!doctype html><html><body>{Marker}</body></html>");
        _api = new ApiFactory { Search = new FakeBookSearch(), WebRoot = _webRoot };
        _client = _api.Client();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/search")]
    [InlineData("/book/OL27482W")]
    public async Task ClientRoutes_GetTheSpa(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(Marker);
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api/books/search/extra")]
    public async Task UnknownApiRoutes_AreStillJson404s(string path) =>
        await (await _client.GetAsync(path, TestContext.Current.CancellationToken)).ShouldBeProblem(HttpStatusCode.NotFound, "not_found");

    [Fact]
    public async Task ApiDocsAndApi_WinOverTheSpa()
    {
        var openApi = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        openApi.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var scalar = await _client.GetAsync("/scalar/", TestContext.Current.CancellationToken);
        scalar.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await scalar.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldNotContain(Marker);
        html.ShouldContain("Find That Book API");

        (await _client.GetAsync("/api/health", TestContext.Current.CancellationToken)).Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }
}
