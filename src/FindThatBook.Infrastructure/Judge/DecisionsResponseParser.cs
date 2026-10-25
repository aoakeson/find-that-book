using System.Text.Json;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;

namespace FindThatBook.Infrastructure.Judge;

/// <summary>
/// Reads a 200 response. The number used is <c>probabilities[value == choice]</c>, never <c>confidence</c>: the 0.9
/// threshold was calibrated on probability and confidence is on a lower scale (AI-40). A choice outside what was sent
/// (blank, unknown) is no verdict (AI-47). <see cref="JudgeStatus.Agreed"/>/<see cref="JudgeStatus.Disagreed"/> only
/// say whether the pick is code's #1; what that means for status (threshold, tiers) is StatusPolicy's call.
/// </summary>
public static class DecisionsResponseParser
{
    public static JudgeVerdict Parse(string json, IReadOnlyList<string> sentIds, string model, long elapsedMs)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Unavailable("bad_json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unavailable("bad_json");
            var usedModel = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : model;
            long? inputTokens = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt64(out var n) ? n : null;

            if (!root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array)
                return Unavailable("no_answers");
            var answer = answers.EnumerateArray().FirstOrDefault(a => a.ValueKind == JsonValueKind.Object
                && a.TryGetProperty("name", out var name) && name.ValueEquals(DecisionsRequestBuilder.QuestionName));
            if (answer.ValueKind != JsonValueKind.Object) return Unavailable("no_answer");

            var type = answer.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "refusal") return new JudgeVerdict(JudgeStatus.Refused, Model: usedModel, InputTokens: inputTokens, ElapsedMs: elapsedMs, Detail: "refusal");
            if (type != "choice") return Unavailable($"answer_type={type ?? "none"}");

            var choice = answer.TryGetProperty("choice", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var isNone = choice == DecisionsRequestBuilder.NoneValue;
            if (string.IsNullOrWhiteSpace(choice) || (!isNone && !sentIds.Contains(choice, StringComparer.Ordinal)))
                return Unavailable("unknown_choice");

            double? probability = null;
            if (answer.TryGetProperty("probabilities", out var probs) && probs.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in probs.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                        && v.GetString() == choice && p.TryGetProperty("probability", out var pr) && pr.TryGetDouble(out var d))
                    {
                        probability = d;
                        break;
                    }
                }
            }
            if (probability is not { } prob || !double.IsFinite(prob) || prob < 0 || prob > 1)
                return Unavailable("no_probability");

            var status = isNone ? JudgeStatus.ChoseNone
                : sentIds.Count > 0 && sentIds[0] == choice ? JudgeStatus.Agreed
                : JudgeStatus.Disagreed;
            return new JudgeVerdict(status, isNone ? null : choice, prob, usedModel, inputTokens, elapsedMs);
        }

        JudgeVerdict Unavailable(string detail) => new(JudgeStatus.Unavailable, Model: model, ElapsedMs: elapsedMs, Detail: detail);
    }
}
