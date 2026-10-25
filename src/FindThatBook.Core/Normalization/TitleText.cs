using System.Text.RegularExpressions;

namespace FindThatBook.Core.Normalization;

/// <summary>Main title / subtitle handling and title keys.</summary>
public static partial class TitleText
{
    /// <summary>Subtitle separators. No bare " or " split: it gained nothing when measured and breaks "Sink or Swim".</summary>
    private static readonly Regex[] SubtitleSeparators =
    [
        new(@"\s*:\s*", RegexOptions.CultureInvariant),
        new(@"\s*;\s*", RegexOptions.CultureInvariant),
        new(@"\s+[-–—]\s+", RegexOptions.CultureInvariant),
        new(@"\.\s+or,?\s+", RegexOptions.CultureInvariant),
        new(@",\s+or,?\s+", RegexOptions.CultureInvariant),
    ];

    /// <summary>Folded main title and subtitle, after removing [..] and (..) groups.</summary>
    public static (string Main, string Subtitle) Split(string title)
    {
        var t = TextFolding.Fold(title);
        t = SquareGroup().Replace(t, " ");   // [collection/set] [adaptation] [1/2]
        t = RoundGroup().Replace(t, " ");    // (twilight, #1) (annotated) (series)
        t = Whitespace().Replace(t, " ").Trim();
        Match? best = null;
        foreach (var separator in SubtitleSeparators)
        {
            // Only each separator's first occurrence counts, and never at position 0.
            var m = separator.Match(t);
            if (m.Success && m.Index > 0 && (best is null || m.Index < best.Index)) best = m;
        }
        return best is null ? (t, "") : (t[..best.Index], t[(best.Index + best.Length)..]);
    }

    /// <summary>Main-title content tokens (numerals as digits) joined by spaces.</summary>
    public static string Key(string title)
    {
        var main = TextFolding.Tokens(Split(title).Main);
        var key = string.Join(' ', Numerals.ToDigits(TextFolding.Content(main)));
        return key.Length > 0 ? key : string.Join(' ', main);
    }

    /// <summary>(main tokens, main + subtitle + subtitle-field tokens), numerals as digits.</summary>
    public static TitleTokens Sets(string title, string? subtitle = null)
    {
        var (mainText, subText) = Split(title);
        var mainTokens = TextFolding.Tokens(mainText);
        var content = TextFolding.Content(mainTokens);
        var main = Numerals.ToDigits(content.Count > 0 ? content : mainTokens);
        var rest = TextFolding.Content(TextFolding.Tokens(subText));
        if (subtitle is not null) rest = [.. rest, .. TextFolding.Content(TextFolding.Tokens(subtitle))];
        return new TitleTokens(main, [.. main, .. Numerals.ToDigits(rest)]);
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex SquareGroup();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex RoundGroup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public sealed record TitleTokens(IReadOnlyList<string> Main, IReadOnlyList<string> Full);
