using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FindThatBook.Api.Tests.Support;

public static class Wire
{
    /// <summary>The API's wire format, made strict: a missing, extra or misspelled field fails deserialization.</summary>
    public static JsonSerializerOptions StrictContract { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static async Task<JsonObject> Json(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException("Empty body");
    }

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), StrictContract)!;

    /// <summary>The catalog's invariants for every error: problem+json, the code, and trace_id = X-Request-Id.</summary>
    public static async Task<JsonObject> ShouldBeProblem(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var json = JsonNode.Parse(body)!.AsObject();
        json["status"]!.GetValue<int>().ShouldBe((int)status);
        json["code"]!.GetValue<string>().ShouldBe(code);
        json["type"]!.GetValue<string>().ShouldBe("/problems/" + code.Replace('_', '-'));
        json["title"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        var traceId = json["trace_id"]!.GetValue<string>();
        traceId.ShouldNotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Request-Id").Single().ShouldBe(traceId);
        json.ContainsKey("traceId").ShouldBeFalse();
        return json;
    }

    public static string[] Errors(this JsonObject problem, string field) =>
        problem["errors"]?[field]?.AsArray().Select(e => e!.GetValue<string>()).ToArray() ?? [];

    public static HttpRequestMessage Get(string url, string? socketIp = null, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (socketIp is not null) request.Headers.Add(ApiFactory.SocketHeader, socketIp);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }
}
