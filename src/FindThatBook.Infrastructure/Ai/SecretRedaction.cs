using System.Text.RegularExpressions;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// The one masking rule for anything that might carry a key before it is logged or returned: provider key shapes
/// (sk-…, AIza…) and any long token-like run. Provider error messages can echo a masked or partial key (AI-49).
/// </summary>
public static partial class SecretRedaction
{
    public const string Mask = "[redacted]";

    public static string Redact(string text) => KeyLikePattern().Replace(text, Mask);

    /// <summary>First line of a provider error, at most <paramref name="maxChars"/> characters, redacted.</summary>
    public static string Excerpt(string message, int maxChars = 160)
    {
        var line = message.Split('\n', 2)[0].Trim();
        if (line.Length > maxChars) line = line[..maxChars];
        return Redact(line);
    }

    [GeneratedRegex(@"(sk-[A-Za-z0-9_\-*]{4,}|AIza[0-9A-Za-z_\-]{8,}|[A-Za-z0-9_\-]{32,})")]
    private static partial Regex KeyLikePattern();
}
