using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

public sealed class OpenLibraryOptionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(kv => new KeyValuePair<string, string?>($"OpenLibrary:{kv.Key}", kv.Value)))
            .Build();
        return new ServiceCollection().AddLogging().AddOpenLibrary(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Defaults_are_valid()
    {
        using var services = Build([]);
        services.GetRequiredService<IStartupValidator>().Validate();
        var o = services.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;

        o.BaseUrl.ShouldBe("https://openlibrary.org/");
        o.UserAgent.ShouldBe("FindThatBook/0.1 (+https://github.com/aoakeson/find-that-book; andrew@andrewoakeson.com)");
        o.RequestsPerSecond.ShouldBe(3);
        o.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(8));
        o.TotalTimeout.ShouldBe(TimeSpan.FromSeconds(15));
        o.MaxRetries.ShouldBe(2);
        o.BreakerMinimumThroughput.ShouldBe(10);
        o.SearchCacheDuration.ShouldBe(TimeSpan.FromHours(1));
        o.EmptySearchCacheDuration.ShouldBe(TimeSpan.FromMinutes(10)); // SY-28
        o.MaxRequestsPerCall.ShouldBe(4);
        services.GetRequiredService<IBookCatalog>().ShouldBeOfType<OpenLibraryCatalog>();
    }

    [Fact]
    public void Binds_from_the_OpenLibrary_section()
    {
        using var services = Build(new()
        {
            ["BaseUrl"] = "http://127.0.0.1:8090", ["AttemptTimeout"] = "00:00:05", ["SlowEmptyThreshold"] = "00:00:04", ["MaxRetries"] = "1",
        });
        var o = services.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;

        o.BaseUrl.ShouldBe("http://127.0.0.1:8090");
        o.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        o.MaxRetries.ShouldBe(1);
        o.MaxRequestsPerCall.ShouldBe(3);
    }

    [Theory]
    [InlineData("BaseUrl", "not a url", "BaseUrl")]
    [InlineData("BaseUrl", "ftp://openlibrary.org/", "BaseUrl")]
    [InlineData("UserAgent", " ", "UserAgent")]
    [InlineData("RequestsPerSecond", "0", "RequestsPerSecond")]
    [InlineData("RequestsPerSecond", "50", "RequestsPerSecond")]
    [InlineData("QueueLimit", "-1", "QueueLimit")]
    [InlineData("QueueLimit", "30", "QueueLimit")]                         // 10 s wait + 8 s attempt > 15 s budget
    [InlineData("AttemptTimeout", "00:00:00", "AttemptTimeout")]
    [InlineData("TotalTimeout", "00:00:05", "TotalTimeout")]               // shorter than the 8 s attempt
    [InlineData("MaxRetries", "0", "MaxRetries")]
    [InlineData("MaxRetryAfter", "00:00:20", "MaxRetryAfter")]             // longer than the 15 s budget
    [InlineData("SlowEmptyThreshold", "00:00:08", "SlowEmptyThreshold")]   // equal to the attempt timeout: dead rule
    [InlineData("BreakerMinimumThroughput", "1", "BreakerMinimumThroughput")]
    [InlineData("BreakerFailureRatio", "0", "BreakerFailureRatio")]
    [InlineData("BreakerSamplingDuration", "00:00:10", "BreakerSamplingDuration")] // under 2 x 8 s
    [InlineData("BreakerDuration", "00:00:00.100", "BreakerDuration")]
    [InlineData("SearchCacheDuration", "-00:00:01", "SearchCacheDuration")]
    public void SY_36_bad_config_fails_at_startup(string key, string value, string reported)
    {
        using var services = Build(new() { [key] = value });

        var ex = Should.Throw<Exception>(() => services.GetRequiredService<IStartupValidator>().Validate());

        // One OptionsValidationException, or an AggregateException when the resilience options (which read ours) fail too.
        var failures = ex is AggregateException all ? all.InnerExceptions : [ex];
        failures.ShouldAllBe(e => e is OptionsValidationException);
        failures.ShouldContain(e => e.Message.Contains($"OpenLibrary:{reported}"));
    }
}
