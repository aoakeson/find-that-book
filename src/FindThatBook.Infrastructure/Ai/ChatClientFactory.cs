using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using GenAi = Google.GenAI;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Builds (and caches) one SDK-backed <see cref="IChatClient"/> per provider, model and key fingerprint.</summary>
public interface IChatClientFactory
{
    IChatClient Get(ProviderTarget target);
}

/// <summary>
/// The official SDKs, each with its hidden retries off (AI-10: the OpenAI SDK sent 4 POSTs for one 429). A 429 is
/// handled by the budget's cooldown instead, and the user's 4 s are not spent waiting.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);
    private readonly HttpMessageHandler? _handler;

    public ChatClientFactory() { }

    /// <summary>Tests: every SDK sends through <paramref name="handler"/> (no network).</summary>
    internal ChatClientFactory(HttpMessageHandler handler) => _handler = handler;

    public IChatClient Get(ProviderTarget target) => _clients.GetOrAdd(target.Identity, _ => Create(target));

    private IChatClient Create(ProviderTarget target)
    {
        var key = target.Key.Value ?? throw new InvalidOperationException($"No API key for {target.Provider}.");
        var inner = target.Provider switch
        {
            AiProviders.Gemini => CreateGemini(key, target.Model),
            AiProviders.Anthropic => CreateAnthropic(key, target.Model),
            AiProviders.OpenAi => CreateOpenAi(key, target.Model),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Provider, "Unknown provider."),
        };
        // GenAI semantic-convention spans/metrics; free without a listener. Message content is never recorded.
        return inner.AsBuilder().UseOpenTelemetry(sourceName: "FindThatBook.Ai", configure: c => c.EnableSensitiveData = false).Build();
    }

    private IChatClient CreateGemini(string key, string model)
    {
        var http = new GenAi.Types.HttpOptions
        {
            // null retry options already mean 1 attempt in 1.27; explicit so an SDK default change can't add retries.
            RetryOptions = new GenAi.Types.HttpRetryOptions { Attempts = 1 },
        };
        GenAi.Types.ClientOptions? client = _handler is null ? null : new() { HttpClientFactory = () => new HttpClient(_handler, disposeHandler: false) };
        // vertexAI: false so GOOGLE_GENAI_USE_VERTEXAI in the environment cannot switch backends.
        return new GenAi.Client(vertexAI: false, apiKey: key, httpOptions: http, clientOptions: client).AsIChatClient(model);
    }

    private IChatClient CreateAnthropic(string key, string model)
    {
        var client = _handler is null
            ? new AnthropicClient { ApiKey = key, MaxRetries = 0 }
            : new AnthropicClient { ApiKey = key, MaxRetries = 0, HttpClient = new HttpClient(_handler, disposeHandler: false) };
        return client.AsIChatClient(model);
    }

    private IChatClient CreateOpenAi(string key, string model)
    {
        var options = new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) };
        if (_handler is not null) options.Transport = new HttpClientPipelineTransport(new HttpClient(_handler, disposeHandler: false));
        return new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(key), options).AsIChatClient();
    }
}
