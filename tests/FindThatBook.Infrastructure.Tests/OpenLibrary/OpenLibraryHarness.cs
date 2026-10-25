using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using System.Web;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace FindThatBook.Infrastructure.Tests.OpenLibrary;

/// <summary>
/// A WireMock Open Library plus the real AddOpenLibrary registration, with small timeouts so the suite runs in
/// seconds. Every test gets its own server, cache, breaker and token bucket.
/// </summary>
internal sealed class OpenLibraryHarness : IAsyncDisposable
{
    /// <summary>
    /// Fast defaults; a test overrides only what it is about. Margins are wide (seconds, not milliseconds) because
    /// the suite also runs while other builds load the CPU: a "fast" scripted reply must never look slow.
    /// </summary>
    private static readonly Dictionary<string, string?> FastDefaults = new()
    {
        ["AttemptTimeout"] = "00:00:04",
        ["TotalTimeout"] = "00:00:10",
        ["RetryBaseDelay"] = "00:00:00.010",
        ["SlowEmptyThreshold"] = "00:00:01.500",
        ["RequestsPerSecond"] = "10",
        ["QueueLimit"] = "20",
    };

    private readonly ServiceProvider _services;

    private OpenLibraryHarness(WireMockServer server, ServiceProvider services, RecordingHybridCache cache)
    {
        Server = server;
        _services = services;
        Cache = cache;
    }

    public WireMockServer Server { get; }

    public RecordingHybridCache Cache { get; }

    public OpenLibraryOptions Options => _services.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;

    /// <summary>A fresh typed client each time, as a request scope would get.</summary>
    public IBookCatalog Catalog => _services.GetRequiredService<IBookCatalog>();

    public static OpenLibraryHarness Create(Dictionary<string, string?>? settings = null, RateLimiter? limiter = null, string? baseUrl = null)
    {
        var server = WireMockServer.Start();
        var values = new Dictionary<string, string?>(FastDefaults) { ["BaseUrl"] = baseUrl ?? server.Url + "/" };
        foreach (var (key, value) in settings ?? []) values[key] = value;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(kv => new KeyValuePair<string, string?>($"{OpenLibraryOptions.Section}:{kv.Key}", kv.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        if (limiter is not null) services.AddSingleton(new OpenLibraryRateLimiter(limiter));
        services.AddOpenLibrary(configuration);
        var defaultCache = services.Single(d => d.ServiceType == typeof(HybridCache)).ImplementationType!;
        RecordingHybridCache? recording = null;
        services.Replace(ServiceDescriptor.Singleton<HybridCache>(sp =>
            recording = new RecordingHybridCache((HybridCache)ActivatorUtilities.CreateInstance(sp, defaultCache))));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        provider.GetRequiredService<HybridCache>();
        return new OpenLibraryHarness(server, provider, recording!);
    }

    /// <summary>Every request WireMock received, in order, query decoded.</summary>
    public IReadOnlyList<NameValueCollection> Requests =>
        [.. Server.LogEntries.OrderBy(e => e.RequestMessage!.DateTime).Select(e => HttpUtility.ParseQueryString(e.RequestMessage!.RawQuery ?? ""))];

    public int RequestCount => Server.LogEntries.Count();

    public static IRequestBuilder Search() => Request.Create().WithPath("/search.json").UsingGet();

    public static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body);

    /// <summary>Answers matching requests with <paramref name="reply"/>, every time.</summary>
    public void Always(Reply reply, IRequestBuilder? request = null, int priority = 10) => InSequence(request ?? Search(), priority, reply);

    /// <summary>Answers search.json with each reply in turn; the last one repeats.</summary>
    public void InSequence(params Reply[] replies) => InSequence(Search(), 10, replies);

    /// <summary>
    /// A callback rather than WireMock scenarios: a scenario advances only once its (delayed) response is sent, so a
    /// retry fired during the delay would match the delayed step again.
    /// </summary>
    public void InSequence(IRequestBuilder request, int priority, params Reply[] replies)
    {
        var served = 0;
        Server.Given(request).AtPriority(priority).RespondWith(Response.Create().WithCallback(async _ =>
        {
            var reply = replies[Math.Min(Interlocked.Increment(ref served) - 1, replies.Length - 1)];
            if (reply.Delay > TimeSpan.Zero) await Task.Delay(reply.Delay);
            var message = new ResponseMessage
            {
                StatusCode = reply.Status,
                BodyData = new BodyData { BodyAsString = reply.Body, DetectedBodyType = BodyType.String, Encoding = Encoding.UTF8 },
            };
            message.AddHeader("Content-Type", "application/json");
            if (reply.RetryAfter is { } retryAfter) message.AddHeader("Retry-After", retryAfter);
            return (IResponseMessage)message;
        }));
    }

    /// <summary>Serves every recorded fixture, matched on its search parameters (fields and limit ignored).</summary>
    public void ServeFixtures()
    {
        foreach (var fixture in Fixtures.All)
        {
            var request = Search();
            foreach (var (name, value) in fixture.SearchParams) request = request.WithParam(name, value);
            Server.Given(request).AtPriority(10).RespondWith(Json(fixture.BodyJson));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        Server.Stop();
        Server.Dispose();
    }
}

/// <summary>One scripted Open Library answer.</summary>
internal sealed record Reply(int Status, string Body, TimeSpan Delay = default, string? RetryAfter = null)
{
    public static Reply NoHits { get; } = new(200, "{\"numFound\":0,\"start\":0,\"numFoundExact\":true,\"docs\":[]}");

    public static Reply Hits(params string[] workIds) => new(200, new JsonObject
    {
        ["numFound"] = workIds.Length,
        ["docs"] = new JsonArray([.. workIds.Select(id => (JsonNode)new JsonObject { ["key"] = "/works/" + id, ["title"] = "Title " + id })]),
    }.ToJsonString());

    public static Reply Error(int status) => new(status, "{\"error\":\"scripted\"}");

    public static Reply Raw(string body) => new(200, body);

    public Reply After(int milliseconds) => this with { Delay = TimeSpan.FromMilliseconds(milliseconds) };

    public Reply WithRetryAfter(string value) => this with { RetryAfter = value };
}

/// <summary>The recorded responses in tests/fixtures/openlibrary (copied to the test output).</summary>
internal static class Fixtures
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "openlibrary");
    private static readonly Lazy<IReadOnlyList<Fixture>> Loaded = new(Load);

    public static IReadOnlyList<Fixture> All => Loaded.Value;

    public static Fixture Named(string name) => All.Single(f => f.Name == name);

    public static TheoryData<string> Names() => [.. All.Select(f => f.Name)];

    private static IReadOnlyList<Fixture> Load()
    {
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "index.json")))!;
        return [.. index["fixtures"]!.AsArray().Select(entry =>
        {
            var file = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, (string)entry!["file"]!)))!;
            var parameters = entry["params"]!.AsObject().ToDictionary(p => p.Key, p => (string)p.Value!);
            return new Fixture((string)entry["name"]!, parameters, (bool)entry["person_field"]!, (bool?)entry["subject_field"] ?? false,
                (int)entry["num_found"]!, (int)entry["docs"]!, file["body"]!.ToJsonString());
        })];
    }
}

internal sealed record Fixture(string Name, IReadOnlyDictionary<string, string> Params, bool PersonField, bool SubjectField, int NumFound, int Docs,
    string BodyJson)
{
    private static readonly string[] Matched = ["q", "title", "author", "author_key", "sort"];

    public IEnumerable<(string Name, string Value)> SearchParams => Matched.Where(Params.ContainsKey).Select(k => (k, Params[k]));

    /// <summary>The CatalogQuery that produces this recording's request.</summary>
    public CatalogQuery Query => new()
    {
        Q = Params.GetValueOrDefault("q"),
        Title = Params.GetValueOrDefault("title"),
        Author = Params.GetValueOrDefault("author"),
        AuthorKey = Params.GetValueOrDefault("author_key"),
        Sort = Params.GetValueOrDefault("sort") == "editions" ? CatalogSort.Editions : CatalogSort.Relevance,
        Limit = int.Parse(Params.GetValueOrDefault("limit") ?? "20", System.Globalization.CultureInfo.InvariantCulture),
        IncludePersons = PersonField,
        IncludeSubjects = SubjectField,
    };
}

/// <summary>Wraps the real HybridCache and records the lifetime of every entry written.</summary>
internal sealed class RecordingHybridCache(HybridCache inner) : HybridCache
{
    public ConcurrentQueue<(string Key, TimeSpan? Expiration)> Writes { get; } = new();

    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
        inner.GetOrCreateAsync(key, (State: state, Factory: factory, Writes, Key: key, Options: options), static async (s, ct) =>
        {
            var value = await s.Factory(s.State, ct);
            s.Writes.Enqueue((s.Key, s.Options?.Expiration));
            return value;
        }, options, tags, cancellationToken);

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        Writes.Enqueue((key, options?.Expiration));
        return inner.SetAsync(key, value, options, tags, cancellationToken);
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => inner.RemoveAsync(key, cancellationToken);

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => inner.RemoveByTagAsync(tag, cancellationToken);
}

/// <summary>
/// A raw socket "Open Library" that answers 200 with headers promising more body than it sends, then hangs up:
/// the client fails mid-body with an HttpIOException. One connection per request (Connection: close).
/// </summary>
internal sealed class BrokenBodyServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly bool _chunked;
    private readonly Task _loop;
    private int _connections;

    private BrokenBodyServer(bool chunked)
    {
        _chunked = chunked;
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

    public int Connections => Volatile.Read(ref _connections);

    public static BrokenBodyServer Start(bool chunked) => new(chunked);

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var request = "";
                while (!request.Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0) break;
                    request += Encoding.ASCII.GetString(buffer, 0, read);
                }
                var partial = "{\"numFound\":2,\"docs\":[{\"key\":\"/works/OL1W\",\"title\":\"Cut o";
                var head = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\n" + (_chunked
                    ? $"Transfer-Encoding: chunked\r\n\r\n{4096:x}\r\n{partial}"
                    : $"Content-Length: 4096\r\n\r\n{partial}");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
                await stream.FlushAsync(_stop.Token);
                client.Client.Shutdown(SocketShutdown.Both);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // stopped
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}

/// <summary>Counts token requests, to prove the throttle sits inside the retry loop.</summary>
internal sealed class CountingLimiter(RateLimiter inner) : RateLimiter
{
    private int _acquisitions;

    public int Acquisitions => Volatile.Read(ref _acquisitions);

    public override TimeSpan? IdleDuration => inner.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => inner.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        Interlocked.Increment(ref _acquisitions);
        return inner.AttemptAcquire(permitCount);
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _acquisitions);
        return inner.AcquireAsync(permitCount, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
