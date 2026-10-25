using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FindThatBook.Infrastructure.Tests.Ai;

/// <summary>A scripted IChatClient: no network, counts calls, keeps what was sent.</summary>
internal sealed class FakeChatClient(Func<IList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    public int Calls;
    public ChatOptions? LastOptions { get; private set; }
    public IList<ChatMessage>? LastMessages { get; private set; }

    public static FakeChatClient Json(string json, ChatFinishReason? finish = null, long input = 120, long output = 30) =>
        new((_, _, _) => Task.FromResult(Response(json, finish, input, output)));

    public static FakeChatClient Throws(Exception ex) => new((_, _, _) => Task.FromException<ChatResponse>(ex));

    public static ChatResponse Response(string text, ChatFinishReason? finish = null, long input = 120, long output = 30) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finish ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        };

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        LastMessages = messages.ToList();
        LastOptions = options;
        return await respond(LastMessages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal sealed class FakeChatClientFactory(IChatClient client) : IChatClientFactory
{
    public List<ProviderTarget> Targets { get; } = [];

    public IChatClient Get(ProviderTarget target)
    {
        lock (Targets) Targets.Add(target);
        return client;
    }
}

/// <summary>Builds the real AI services around a fake chat client and in-memory configuration.</summary>
internal sealed class AiTestHost : IAsyncDisposable
{
    public const string GeminiKey = "test-gemini-key-0000";
    public const string AnthropicKey = "test-anthropic-key-1111";
    public const string OpenAiKey = "test-openai-key-2222";

    private readonly ServiceProvider _sp;

    public AiTestHost(IChatClient client, Dictionary<string, string?>? settings = null, IChatClientFactory? factory = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Provider"] = "gemini",
            ["AI:Gemini:ApiKey"] = GeminiKey,
            ["AI:Anthropic:ApiKey"] = AnthropicKey,
            ["AI:OpenAI:ApiKey"] = OpenAiKey,
            ["AI:Gemini:RequestsPerMinute"] = "0",
            ["AI:Anthropic:RequestsPerMinute"] = "0",
            ["AI:OpenAI:RequestsPerMinute"] = "0",
        };
        foreach (var (k, v) in settings ?? []) values[k] = v;
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Factory = factory ?? new FakeChatClientFactory(client);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(new FindThatBook.Core.Search.SearchOptions());
        services.AddSingleton(Factory);
        services.AddAi(Configuration);
        _sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.Zero };
    public IConfigurationRoot Configuration { get; }
    public IChatClientFactory Factory { get; }

    public StructuredLlm Llm => _sp.GetRequiredService<StructuredLlm>();
    public AiSettingsStore Settings => _sp.GetRequiredService<AiSettingsStore>();
    public IAiAdmin Admin => _sp.GetRequiredService<IAiAdmin>();
    public AiBudget Budget => _sp.GetRequiredService<AiBudget>();
    public IQueryInterpreter Interpreter => _sp.GetRequiredService<IQueryInterpreter>();
    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();

    public static StructuredCall Call(string user = "\"the hobbit\"", string system = "system v1", string promptId = "test.v1",
        string operation = "interpret", int maxTokens = 256, TimeSpan? timeout = null) =>
        new(operation, promptId, system, user, maxTokens, timeout ?? TimeSpan.FromSeconds(4));

    public ValueTask DisposeAsync() => _sp.DisposeAsync();
}

/// <summary>A tiny output record for StructuredLlm tests.</summary>
public sealed record TestOutput(
    [property: System.Text.Json.Serialization.JsonPropertyName("title")] string? Title,
    [property: System.Text.Json.Serialization.JsonPropertyName("tags")] string[] Tags);

/// <summary>Counts HTTP requests and answers each with a fixed status, keeping the request bodies (real SDKs, no network).</summary>
internal sealed class CountingHandler(System.Net.HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? configure = null) : HttpMessageHandler
{
    public int Requests;
    public List<string> Bodies { get; } = [];
    public List<HttpRequestMessage> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        lock (Bodies)
        {
            Sent.Add(request);
        }
        var text = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Bodies) Bodies.Add(text);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        configure?.Invoke(response);
        return response;
    }
}

internal static class NullOptions
{
    public static IOptions<T> Of<T>(T value) where T : class => Options.Create(value);
    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;
    public static HybridCache Cache() => new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
}
