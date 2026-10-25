using System.Globalization;

namespace FindThatBook.Api.Endpoints;

/// <summary>
/// Query-string rules shared by the endpoints. Parameters are bound as strings and parsed here, so a bad value
/// ("limit=abc", "5.0", "1e9") is a 400 that names the field instead of a bare binding failure (SY-11).
/// </summary>
internal static class QueryParameters
{
    /// <summary>"?q=a&amp;q=b" would otherwise be joined into "a,b" silently (SY-13).</summary>
    public static bool IsRepeated(HttpRequest request, string name) => request.Query[name].Count > 1;

    /// <summary>Adds an error under <paramref name="field"/> unless the value is absent or a whole number in range.</summary>
    public static int? ParseLimit(HttpRequest request, string name, string field, int min, int max, IDictionary<string, string[]> errors)
    {
        var values = request.Query[name];
        if (values.Count == 0) return null;
        if (values.Count > 1)
        {
            errors[field] = [$"Give {name} only once."];
            return null;
        }
        var raw = values[0]?.Trim() ?? "";
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max) return n;
        errors[field] = [$"The field {field} must be a whole number between {min} and {max}."];
        return null;
    }
}
