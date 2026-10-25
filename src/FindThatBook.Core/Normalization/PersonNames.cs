using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace FindThatBook.Core.Normalization;

/// <summary>Author name tokens, identity keys and surnames.</summary>
public static partial class PersonNames
{
    /// <summary>Generational suffixes a surname never is ("Kurt Vonnegut Jr.", "Dumas fils"): qa/02 author-identity rule, IN-065.</summary>
    private static readonly FrozenSet<string> Suffixes = FrozenSet.ToFrozenSet(["jr", "sr", "ii", "iii", "iv", "fils", "pere"]);

    /// <summary>Honorifics dropped from the identity key so "Dr. Seuss" and "Seuss" merge (DA-021).</summary>
    private static readonly FrozenSet<string> Honorifics = FrozenSet.ToFrozenSet(["dr", "mr", "mrs", "ms", "sir", "dame", "rev", "prof"]);

    /// <summary>Folded name tokens; "Tolkien, J. R. R. 1892-1973." is un-inverted and loses its dates.</summary>
    public static IReadOnlyList<string> Tokens(string name)
    {
        var n = TextFolding.Fold(name);
        // "Vonnegut, Kurt, Jr." and "Kurt Vonnegut, Jr.": set a trailing suffix aside so the inversion check sees the name.
        var suffix = "";
        var trailing = TrailingSuffix().Match(n);
        if (trailing.Success)
        {
            suffix = " " + trailing.Groups[1].Value;
            n = n[..trailing.Index];
        }
        var comma = n.IndexOf(',');
        if (comma >= 0 && !NotInverted().IsMatch(n[(comma + 1)..]))
        {
            var first = Dates().Replace(n[(comma + 1)..], "");
            n = first + " " + n[..comma];
        }
        n += suffix;
        // Apostrophes and elisions as in TextFolding.Tokens, so a typed "l'engle" and the record "L'Engle" agree.
        return TextFolding.UnfoldedTokens(n.Replace("&", " "));
    }

    /// <summary>
    /// Identity of an author across records: joined initials + the rest ("jrr tolkien"), with
    /// honorifics dropped and repeated tokens collapsed (DA-021).
    /// </summary>
    public static string Key(string name)
    {
        var toks = Tokens(name);
        var initials = string.Concat(toks.Where(t => t.Length == 1));
        var rest = toks.Where(t => t.Length > 1).ToList();
        var named = rest.Where(t => !Honorifics.Contains(t)).Distinct().ToList();
        if (named.Count > 0) rest = named;
        if (initials.Length > 0) rest.Insert(0, initials);
        return string.Join(' ', rest);
    }

    /// <summary>Last token longer than one letter that is not a generational suffix ("vonnegut", not "jr").</summary>
    public static string Surname(string name)
    {
        var toks = Tokens(name).Where(t => t.Length > 1).ToList();
        return toks.LastOrDefault(t => !Suffixes.Contains(t)) ?? toks.LastOrDefault() ?? "";
    }

    [GeneratedRegex(@"\b(?:jr|sr|baron|lord|sir)\b")]
    private static partial Regex NotInverted();

    [GeneratedRegex(@",\s*(jr|sr|ii|iii|iv)\b\.?\s*$")]
    private static partial Regex TrailingSuffix();

    [GeneratedRegex(@"\b\d{3,4}\b.*")]
    private static partial Regex Dates();
}
