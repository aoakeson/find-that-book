using System.Collections.Frozen;
using System.Globalization;

namespace FindThatBook.Core.Normalization;

/// <summary>Number words ↔ digits, for title comparison and for the text of the broad search.</summary>
public static class Numerals
{
    private static readonly FrozenDictionary<string, int> Units =
        "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen"
            .Split(' ').Select((w, i) => (w, i)).ToFrozenDictionary(p => p.w, p => p.i);

    private static readonly FrozenDictionary<string, int> Tens =
        "twenty thirty forty fifty sixty seventy eighty ninety"
            .Split(' ').Select((w, i) => (w, i)).ToFrozenDictionary(p => p.w, p => 10 * (p.i + 2));

    /// <summary>["nineteen","eighty","four"] → ["1984"]; "thirty nine" → 39; "twenty thousand" → 20000.</summary>
    public static IReadOnlyList<string> ToDigits(IReadOnlyList<string> tokens)
    {
        if (!tokens.Any(IsNumberWord)) return tokens;
        var output = new List<string>(tokens.Count);
        var i = 0;
        while (i < tokens.Count)
        {
            var (value, next) = Run(tokens, i);
            if (value is null)
            {
                output.Add(tokens[i]);
                i++;
            }
            else
            {
                output.Add(value.Value.ToString(CultureInfo.InvariantCulture));
                i = next;
            }
        }
        return output;
    }

    /// <summary>
    /// Like <see cref="ToDigits"/> but a lone "one"/"zero" (no number word next to it) stays a word: "the one with
    /// pigs" keeps "one". The broad search sends this digit form.
    /// </summary>
    public static IReadOnlyList<string> ToDigitsForSearch(IReadOnlyList<string> tokens)
    {
        const string marker = "\0";
        var marked = tokens.Select((t, i) =>
            t is "one" or "zero" && !((i > 0 && IsRunWord(tokens[i - 1])) || (i + 1 < tokens.Count && IsRunWord(tokens[i + 1])))
                ? marker + t
                : t).ToList();
        return [.. ToDigits(marked).Select(t => t.StartsWith(marker, StringComparison.Ordinal) ? t[1..] : t)];
    }

    /// <summary>Value of a number word 0–19 or a tens word, else null.</summary>
    public static int? WordValue(string token) =>
        Units.TryGetValue(token, out var u) ? u : Tens.TryGetValue(token, out var t) ? t : null;

    private static bool IsNumberWord(string t) => Units.ContainsKey(t) || Tens.ContainsKey(t);

    private static bool IsRunWord(string t) => IsNumberWord(t) || t is "hundred" or "thousand";

    /// <summary>
    /// Parses a run of number words starting at <paramref name="start"/>. Year style is exactly two
    /// 2-digit chunks: "nineteen eighty four" → 1984, "eighteen twelve" → 1812; anything else chunked is not a number.
    /// </summary>
    private static (int? Value, int Next) Run(IReadOnlyList<string> toks, int start)
    {
        var chunks = new List<int>();
        int? cur = null;
        var total = 0;
        var j = start;
        while (j < toks.Count)
        {
            var t = toks[j];
            if (Units.TryGetValue(t, out var u))
            {
                if (cur is null) cur = u;
                else if (cur % 100 == 0 && cur >= 100) cur += u;                                  // hundred + four
                else if (cur >= 20 && cur % 10 == 0 && cur % 100 >= 20 && u < 10) cur += u;       // twenty + one
                else { chunks.Add(cur.Value); cur = u; }                                           // nineteen | four
            }
            else if (Tens.TryGetValue(t, out var tens))
            {
                if (cur is null) cur = tens;
                else if (cur % 100 == 0 && cur >= 100) cur += tens;                               // hundred + fifty
                else { chunks.Add(cur.Value); cur = tens; }                                        // nineteen | eighty
            }
            else if (t == "hundred" && cur is not null && chunks.Count == 0) cur *= 100;
            else if (t == "thousand" && cur is not null && chunks.Count == 0) { total += cur.Value * 1000; cur = null; }
            else if (t == "and" && ((cur is null && total != 0) || (cur is >= 100 && cur % 100 == 0))
                     && j + 1 < toks.Count && IsNumberWord(toks[j + 1]))
            {
                // one hundred and one
            }
            else break;
            j++;
        }
        if (cur is null && total == 0) return (null, start);
        var last = cur ?? 0;
        if (chunks.Count > 0)
        {
            if (chunks.Count != 1 || chunks[0] is < 10 or > 99 || last is < 0 or > 99) return (null, start);
            return (chunks[0] * 100 + last, j);
        }
        return (total + last, j);
    }
}
