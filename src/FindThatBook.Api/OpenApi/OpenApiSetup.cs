using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FindThatBook.Api.OpenApi;

/// <summary>OpenAPI 3.1 at /openapi/v1.json (every environment), with descriptions, parameter rules and named examples.</summary>
internal static class OpenApiSetup
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) => services.AddOpenApi(DocumentName, o =>
    {
        o.AddDocumentTransformer(DescribeDocument);
        o.AddOperationTransformer(DescribeParameters);
        o.AddOperationTransformer(AttachExamples);
    });

    private static Task DescribeDocument(OpenApiDocument doc, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        doc.Info.Title = "Find That Book API";
        doc.Info.Version = "v1";
        doc.Info.Description = """
            Find the book a messy text most likely refers to ("tolkien hobbit illustrated deluxe 1937", "mark huckleberry").
            **AI interprets, code decides, every step is measured**: an LLM turns the text into a search hypothesis and
            writes one grounded sentence per result; deterministic code searches Open Library, ranks the works on the
            brief's hierarchy (a–e) and decides the status.

            * `GET /api/books/search` always answers 200 for `match`, `ambiguous` and `none`. `degraded: true` means a
              lower-quality answer (LLM unavailable, partial retrieval); the LLM never causes an error status.
            * Errors are RFC 9457 problem details with a stable `code` and the request's `trace_id`
              (also the `X-Request-Id` header and `meta.request_id`). Validation errors are keyed by field (`Q`, `Limit`).
            * JSON is snake_case; enums are snake_case strings.
            """;
        doc.Info.Contact = new OpenApiContact { Name = "Andrew Oakeson", Email = "andrew@andrewoakeson.com" };
        doc.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Books", Description = "Search: interpret, retrieve, rank, explain." },
            new() { Name = "Suggest", Description = "Autocomplete from a local index (no upstream calls)." },
            new() { Name = "System", Description = "Health." },
        };
        return Task.CompletedTask;
    }

    /// <summary>Parameters are bound as strings so bad values become named 400s; the document states the real rules.</summary>
    private static Task DescribeParameters(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            switch (operation.OperationId, parameter.Name)
            {
                case ("SearchBooks", "q"):
                    parameter.Required = true;
                    parameter.Description = "The messy text: title and/or author and/or keywords. 1–300 characters (UTF-16 units) "
                        + "after trimming and removing control/zero-width characters; at least one letter or digit, any script; not only stop words. Give it once.";
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 300 };
                    parameter.Example = JsonValue.Create("tolkien hobbit illustrated deluxe 1937");
                    break;
                case ("SearchBooks", "limit"):
                    parameter.Description = "How many candidates to return (default 5).";
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = "10", Default = JsonValue.Create(5) };
                    break;
                case ("SuggestBooks", "prefix"):
                    parameter.Description = "What has been typed so far, up to 100 characters. Empty returns an empty list.";
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 100 };
                    parameter.Example = JsonValue.Create("har");
                    break;
                case ("SuggestBooks", "limit"):
                    parameter.Description = "How many suggestions to return (default 8).";
                    parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = "10", Default = JsonValue.Create(8) };
                    break;
            }
        }
        return Task.CompletedTask;
    }

    private static Task AttachExamples(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (operation.Responses is null) return Task.CompletedTask;
        foreach (var example in OpenApiExamples.All.Where(e => e.OperationId == operation.OperationId))
        {
            if (!operation.Responses.TryGetValue(example.Status.ToString(System.Globalization.CultureInfo.InvariantCulture), out var response)
                || response.Content is null)
            {
                continue;
            }
            foreach (var media in response.Content.Values)
            {
                media.Examples ??= new Dictionary<string, IOpenApiExample>();
                media.Examples[example.Name] = new OpenApiExample
                {
                    Summary = example.Summary,
                    Description = example.Description,
                    Value = example.Value.DeepClone(),
                };
            }
        }
        return Task.CompletedTask;
    }
}
