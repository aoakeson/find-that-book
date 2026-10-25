using System.Text.Json;
using System.Text.Json.Nodes;

namespace FindThatBook.Api.OpenApi;

/// <summary>
/// Named examples shipped as embedded JSON files <c>OpenApi/Examples/{operationId}.{status}.{name}.json</c>, each
/// <c>{"summary", "description", "value"}</c>. The same files feed Scalar and the drift test that deserializes every
/// value into the real contract types.
/// </summary>
internal static class OpenApiExamples
{
    private const string Prefix = "examples/";

    public sealed record Example(string OperationId, int Status, string Name, string Summary, string? Description, JsonNode Value);

    public static IReadOnlyList<Example> All => Loaded.Value;

    private static readonly Lazy<IReadOnlyList<Example>> Loaded = new(Load);

    private static List<Example> Load()
    {
        var assembly = typeof(OpenApiExamples).Assembly;
        var examples = new List<Example>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var parts = resource[Prefix.Length..^".json".Length].Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var status))
                throw new InvalidOperationException($"Example file name must be operationId.status.name.json: {resource}");
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var file = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow })!.AsObject();
            examples.Add(new Example(parts[0], status, parts[2],
                file["summary"]?.GetValue<string>() ?? parts[2],
                file["description"]?.GetValue<string>(),
                file["value"] ?? throw new InvalidOperationException($"Example without a value: {resource}")));
        }
        return examples;
    }
}
