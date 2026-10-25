using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Core.Ports;

namespace FindThatBook.Infrastructure.Judge;

/// <summary>
/// Builds the choice-only request (the cheapest shape, ~0.25 s p50): the query is data inside <c>input</c> and never
/// in instructions (AI-45), candidates keep code order (input order is what moves the answers, AI-39),
/// ids are deduped and blank ids skipped (AI-38, AI-47), lists and strings are capped (AI-46), and every description
/// carries year + edition count so twin records differ (AI-39). A NONE choice is always appended (AI-43).
/// </summary>
public static class DecisionsRequestBuilder
{
    public const string QuestionName = "best_match";
    public const string NoneValue = "NONE";

    public const string Instructions = "Which candidate work is the patron most likely looking for? Prefer the original work over "
        + "adaptations, study guides, collections or duplicate minor records unless the search asks for those.";

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <returns>The request body and the candidate ids in the order sent (NONE excluded); empty when fewer than 2 remain.</returns>
    public static (JsonObject Body, IReadOnlyList<string> Ids) Build(JudgeRequest request, JudgeOptions o)
    {
        var candidates = request.Candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.WorkId))
            .DistinctBy(c => c.WorkId.Trim(), StringComparer.Ordinal)
            .Take(o.MaxCandidates)
            .ToList();
        var ids = candidates.Select(c => c.WorkId.Trim()).ToList();

        var data = new JsonArray();
        var descriptions = new List<string>();
        foreach (var c in candidates)
        {
            var title = Cap(c.Title.Trim(), o.MaxTitleChars);
            var authors = c.Authors.Where(a => !string.IsNullOrWhiteSpace(a)).Take(o.MaxAuthorsPerCandidate).Select(a => Cap(a.Trim(), 100)).ToList();
            var contributors = c.Contributors.Where(a => !string.IsNullOrWhiteSpace(a)).Take(o.MaxContributorsPerCandidate).Select(a => Cap(a.Trim(), 120)).ToList();
            var item = new JsonObject
            {
                ["id"] = c.WorkId.Trim(),
                ["title"] = title,
                ["authors"] = new JsonArray([.. authors.Select(a => (JsonNode)a)]),
                ["contributors"] = new JsonArray([.. contributors.Select(a => (JsonNode)a)]),
            };
            if (c.FirstPublishYear is { } year) item["first_publish_year"] = year;
            item["edition_count"] = c.EditionCount;
            data.Add(item);
            descriptions.Add(Cap(Describe(title, authors, c.FirstPublishYear, c.EditionCount), o.MaxDescriptionChars));
        }

        // Still-identical descriptions (same title, authors, year and count) get the id, so no two choices read the same.
        var dupes = descriptions.GroupBy(d => d, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var choices = new JsonArray();
        for (var i = 0; i < ids.Count; i++)
        {
            var description = dupes.Contains(descriptions[i]) ? $"{descriptions[i]} [{ids[i]}]" : descriptions[i];
            choices.Add(new JsonObject { ["value"] = ids[i], ["description"] = description });
        }
        choices.Add(new JsonObject { ["value"] = NoneValue, ["description"] = "None of these candidates is the book the patron wants" });

        var query = JsonSerializer.Serialize(Cap(request.Query.Trim(), 300), Relaxed);
        var body = new JsonObject
        {
            ["model"] = o.Model,
            ["input"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = $"A library patron typed this into a book search box: {query}. Below are candidate works "
                            + "returned by Open Library (a work groups all editions of one book).",
                    },
                    new JsonObject { ["type"] = "input_json", ["data"] = data }),
            }),
            ["questions"] = new JsonArray(new JsonObject
            {
                ["type"] = "choice",
                ["name"] = QuestionName,
                ["instructions"] = Instructions,
                ["choices"] = choices,
            }),
        };
        return (body, ids);
    }

    /// <summary>"Pride and Prejudice by Jane Austen (1813), 4,042 editions".</summary>
    public static string Describe(string title, IReadOnlyList<string> authors, int? year, int editions)
    {
        var text = authors.Count > 0 ? $"{title} by {string.Join(", ", authors)}" : title;
        if (year is { } y) text += $" ({y.ToString(CultureInfo.InvariantCulture)})";
        return text + (editions == 1 ? ", 1 edition" : $", {editions.ToString("N0", CultureInfo.InvariantCulture)} editions");
    }

    private static string Cap(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = max - 1; // room for the ellipsis
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut].TrimEnd() + "…";
    }
}
