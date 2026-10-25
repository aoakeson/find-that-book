namespace FindThatBook.Infrastructure.Tests.Judge;

/// <summary>
/// Decisions API responses recorded live (gpt-6-luna, 2026-10-24): a full multi-question body, a NONE pick, a refusal
/// (answers and token count re-wrapped in the response envelope), and P&amp;P twins in swapped order where confidence
/// (0.41) and probability (0.56) differ (AI-39, AI-40).
/// </summary>
internal static class DecisionsFixtures
{
    /// <summary>"tolkien hobbit illustrated deluxe 1937": best_match OL27482W at 1.0 plus three other questions.</summary>
    public const string HobbitChoice = """
        {
            "model": "gpt-6-luna",
            "answers": [
                {
                    "type": "choice",
                    "name": "best_match",
                    "choice": "OL27482W",
                    "probabilities": [
                        { "value": "OL27482W", "probability": 1.0 },
                        { "value": "OL219602W", "probability": 0.0 },
                        { "value": "OL14926019W", "probability": 0.0 },
                        { "value": "OL14933471W", "probability": 0.0 }
                    ],
                    "confidence": 1.0
                },
                { "type": "predicate", "name": "dixon_is_primary_author", "probability": 0.02 },
                { "type": "predicate", "name": "wants_specific_edition", "probability": 0.91 },
                {
                    "type": "score",
                    "name": "query_clarity",
                    "score": 1.97,
                    "probabilities": [
                        { "value": 0, "label": "unclear", "probability": 0.0 },
                        { "value": 1, "label": "somewhat_clear", "probability": 0.03 },
                        { "value": 2, "label": "clear", "probability": 0.97 }
                    ],
                    "confidence": 0.96
                }
            ],
            "usage": {
                "input_tokens": 833,
                "input_tokens_details": { "cached_tokens": 0, "cache_write_tokens": 0 },
                "output_tokens": 0,
                "output_tokens_details": { "reasoning_tokens": 0 },
                "total_tokens": 833
            }
        }
        """;

    public static readonly string[] HobbitIds = ["OL27482W", "OL219602W", "OL14926019W", "OL14933471W"];

    public const string NoneChosen = """
        {"model": "gpt-6-luna", "answers": [{"type": "choice", "name": "best_match", "choice": "NONE", "probabilities": [{"value": "OL59800W", "probability": 0.27}, {"value": "OL24715772W", "probability": 0.01}, {"value": "OL35148039W", "probability": 0.0}, {"value": "OL15330629W", "probability": 0.0}, {"value": "OL42492221W", "probability": 0.0}, {"value": "OL19306603W", "probability": 0.0}, {"value": "NONE", "probability": 0.72}], "confidence": 0.67}],
         "usage": {"input_tokens": 653, "output_tokens": 0, "total_tokens": 653}}
        """;

    public static readonly string[] NoneIds = ["OL59800W", "OL24715772W", "OL35148039W", "OL15330629W", "OL42492221W", "OL19306603W"];

    public const string Refusal = """
        {"model": "gpt-6-luna", "answers": [{"type": "refusal", "name": "best_match"}], "usage": {"input_tokens": 285, "output_tokens": 0, "total_tokens": 285}}
        """;

    /// <summary>Twin first (56 editions), canonical second: the pick is right but probability ≠ confidence.</summary>
    public const string TwinsSwapped = """
        {"model": "gpt-6-luna", "answers": [{"type": "choice", "name": "best_match", "choice": "OL66554W", "probabilities": [{"value": "OL21177W", "probability": 0.44}, {"value": "OL66554W", "probability": 0.56}, {"value": "OL3W", "probability": 0.0}, {"value": "NONE", "probability": 0.0}], "confidence": 0.41}],
         "usage": {"input_tokens": 331, "output_tokens": 0, "total_tokens": 331}}
        """;

    public static readonly string[] TwinIds = ["OL21177W", "OL66554W", "OL3W"];

    /// <summary>AI-49: 401 body (the message echoes a masked key, so only the code is ever logged).</summary>
    public const string InvalidKey = """
        {"error": {"message": "Incorrect API key provided: sk-proj-******d000.", "type": "invalid_request_error", "param": null, "code": "invalid_api_key"}}
        """;

    /// <summary>AI-38: what a builder bug looks like.</summary>
    public const string DuplicateChoices = """
        {"error": {"message": "Choice values must be unique within the question.", "type": "invalid_request_error", "param": "questions[0].choices[1].value", "code": null}}
        """;
}
