using System.Globalization;

namespace FindThatBook.Infrastructure.Suggest;

/// <summary>
/// Digits → English number words, the reverse of the shared numeral folding, so a title written in digits
/// ("20,000 Leagues Under the Sea") is also found by its spoken form ("twenty thousand…"). 1100–1999 read as years
/// when the last two digits are 10 or more ("1984" → "nineteen eighty four"); every output folds back to its digits.
/// </summary>
internal static class NumberWords
{
    static readonly string[] Units =
        "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen".Split(' ');

    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    public const int Max = 999_999;

    /// <summary>0..999,999 in words; null outside that range.</summary>
    public static string? ToWords(int n)
    {
        if (n is < 0 or > Max) return null;
        if (n is >= 1100 and <= 1999 && n % 100 >= 10) return $"{Below100(n / 100)} {Below100(n % 100)}";
        if (n < 1000) return Below1000(n);
        var rest = n % 1000;
        return $"{Below1000(n / 1000)} thousand" + (rest > 0 ? " " + Below1000(rest) : "");
    }

    /// <summary>Each plain number token ("1984", not "007" or "1st") replaced by its words; other tokens unchanged.</summary>
    public static string[] Expand(IReadOnlyList<string> tokens)
    {
        var output = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var words = IsPlainNumber(token) && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? ToWords(n) : null;
            if (words is null) output.Add(token);
            else output.AddRange(words.Split(' '));
        }
        return [.. output];
    }

    static bool IsPlainNumber(string token) =>
        token.Length is > 0 and <= 6 && token.All(char.IsAsciiDigit) && (token.Length == 1 || token[0] != '0');

    static string Below100(int n) => n < 20 ? Units[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Units[n % 10] : "");

    static string Below1000(int n)
    {
        if (n < 100) return Below100(n);
        var rest = n % 100;
        return $"{Units[n / 100]} hundred" + (rest > 0 ? " " + Below100(rest) : "");
    }
}
