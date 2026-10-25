using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Replay;

/// <summary>
/// Saves every successful GET …/search.json response that passes through an HttpClient as {"url","dt","recorded_at","body"},
/// the format <see cref="RecordedResponseIndex"/> reads. Added to every IHttpClientFactory client by the eval host, so it
/// wraps the Open Library adapter's typed client without touching its code; other hosts and paths pass through untouched.
/// Only the URL and the JSON body are written: never headers (no keys can leak into fixtures).
/// </summary>
/// <remarks>
/// The adapter calls HTTP from inside its HybridCache factory, which does not flow the eval's per-query AsyncLocal, so
/// saved files are also reported to a run-wide <see cref="RecordingSink"/> (the record-on-miss cache drains it).
/// </remarks>
public sealed class RecordingHandler(string folder, RecordingSink? sink = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, ct);
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } uri
            || !uri.AbsolutePath.EndsWith("/search.json", StringComparison.Ordinal) || response.StatusCode != HttpStatusCode.OK)
            return response;

        await response.Content.LoadIntoBufferAsync(ct); // buffered content can be read again by the adapter
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? body;
        try { body = JsonNode.Parse(text); }
        catch (JsonException) { return response; }

        var record = new JsonObject
        {
            ["url"] = uri.AbsoluteUri,
            ["dt"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 3),
            ["recorded_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["body"] = body,
        };
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, FileName(uri.AbsoluteUri));
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, record.ToJsonString(), ct);
        File.Move(temp, file, overwrite: true);
        EvalContext.Current?.Recorded.Enqueue(file);
        sink?.Saved.Enqueue(file);
        return response;
    }

    public static string FileName(string url) =>
        "search_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..12] + ".json";
}

/// <summary>Files the recording handler saved during this run, in order (shared by every query of the run).</summary>
public sealed class RecordingSink
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Saved { get; } = new();
}

/// <summary>Fails any HTTP request (offline tests): proves the replay run never reaches the network.</summary>
public sealed class NetworkBlockedHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throw new InvalidOperationException($"Network is disabled in this eval run (request to {request.RequestUri?.Host}).");
}
