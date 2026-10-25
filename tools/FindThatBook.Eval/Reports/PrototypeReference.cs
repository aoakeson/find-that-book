namespace FindThatBook.Eval.Reports;

/// <summary>
/// The Python prototype's published numbers, quoted so a C# run can be compared with them. They are NOT results of
/// this build and every block says so. Source: the research archive's write-ups of the prototype (optional, not part
/// of this repo): the v3 fixes ("Golden and hard set"), the accuracy fixes ("Headline") and the eval plan ("First
/// expected numbers").
/// </summary>
public static class PrototypeReference
{
    private const string Banner =
        "**Reference only: Python prototype numbers from the research phase, NOT measured by this build.** "
        + "They were produced on golden v1 (none-04 scored as a 200 `none`; v2 expects a 400, same 60-row denominator) "
        + "by the prototype's `golden_v3.py` replaying cached live responses from before v3, with LLM-on rows using cached "
        + "gpt-4.1-mini prompt-v1 hypotheses (so the edition-title fix could not fire). Rows are in-sample: the rules were tuned on them.";

    public static string? For(string datasetName, string path)
    {
        var file = Path.GetFileName(path);
        if (file.StartsWith("golden", StringComparison.OrdinalIgnoreCase)) return Golden;
        if (file.StartsWith("hard", StringComparison.OrdinalIgnoreCase)) return Hard;
        return null;
    }

    private static string Golden => $"""
        {Banner}

        | Prototype arm (source) | top-1 / top-5 (57) | status (60) | `match` n @ precision |
        |---|---|---|---|
        | prototype v3, LLM on | .982 / 1.000 | .983 | 45 @ 1.000 |
        | prototype v3, LLM off | .982 / 1.000 | .933 | 39 @ 1.000 |
        | prototype before v3 (first code fixes), LLM on, live | .947 / .982 | .983 | 44 @ 1.000 |
        | prototype before v3 (first code fixes), LLM off, live | .912 / .982 | .950 | 39 @ .974 |
        | raw Open Library order | 45/57 (.79) / 48/57 (.84) | 48/60 (.80) | 53 @ .74 |
        | target set for the C# pipeline before it was built | ≥ .91 / ≥ .95 | ≥ .92 | precision ≥ .97 |

        Noise: one golden query = 1.8 points of top-1.
        """;

    private static string Hard => $"""
        {Banner}

        | Prototype arm | top-1 / top-5 (46) | coverage @ precision |
        |---|---|---|
        | prototype v3, LLM on | 1.000 / 1.000 | .804 @ 1.000 |
        | prototype v3, LLM off | .957 / .957 | .522 @ .958 |

        Noise: one hard-set query = 2.2 points.
        """;
}
