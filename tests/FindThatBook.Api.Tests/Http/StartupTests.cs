using FindThatBook.Api.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests.Http;

/// <summary>SY-36: a configuration that would break every request fails at boot, not on the first search.</summary>
public sealed class StartupTests
{
    [Theory]
    [InlineData("zero deadline", "Search:RequestDeadline", "00:00:00")]
    [InlineData("reserve longer than the deadline", "Search:PostRetrievalReserve", "00:01:00")]
    [InlineData("default limit above max", "Search:DefaultLimit", "50")]
    [InlineData("judge threshold above 1", "Search:JudgeThreshold", "1.5")]
    [InlineData("rate limit of zero", "RateLimit:SearchPermitsPerIp", "0")]
    [InlineData("proxy that is not an IP", "ForwardedHeaders:KnownProxies:0", "render-proxy")]
    [InlineData("network that is not a CIDR", "ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    [InlineData("covers URL that is not a URL", "OpenLibrary:CoversUrl", "covers")]
    public async Task BadConfiguration_FailsAtStartup(string caseId, string key, string value)
    {
        await using var api = new ApiFactory { Search = new FakeBookSearch(), Settings = { [key] = value } };
        Should.Throw<Exception>(() => api.Client(), caseId);
    }

    /// <summary>SY-26: the in-memory cache (Open Library responses, LLM results) has a byte bound, 64 MB unless configured.</summary>
    [Theory]
    [InlineData(null, 64)]
    [InlineData("8", 8)]
    public async Task MemoryCache_IsBounded(string? configured, long expectedMegabytes)
    {
        await using var api = new ApiFactory { Search = new FakeBookSearch() };
        if (configured is not null) api.Settings["Cache:SizeLimitMegabytes"] = configured;

        var options = api.Services.GetRequiredService<IOptions<MemoryCacheOptions>>().Value;

        options.SizeLimit.ShouldBe(expectedMegabytes * 1024 * 1024);
    }
}
