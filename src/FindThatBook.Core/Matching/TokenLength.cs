using System.Text;

namespace FindThatBook.Core.Matching;

/// <summary>Token length in characters (code points), not UTF-16 units, so length thresholds hold in every script.</summary>
internal static class TokenLength
{
    public static int Len(this string token)
    {
        foreach (var ch in token)
            if (char.IsSurrogate(ch)) return token.EnumerateRunes().Count();
        return token.Length;
    }
}
