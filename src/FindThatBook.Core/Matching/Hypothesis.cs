using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

/// <summary>Another title the interpreter gave: the original-language title or the English edition title.</summary>
internal sealed record TitleVariant(bool IsOriginal, string Title, IReadOnlyList<string> Main, IReadOnlyList<string> Full);

/// <summary>
/// The interpreter's hypothesis in comparison form. <see cref="Main"/>/<see cref="Full"/> are the
/// folded title sets, <see cref="Surname"/>/<see cref="AuthorKey"/> the LLM author's identity.
/// </summary>
internal sealed class NormalizedHypothesis
{
    public string? Title { get; private init; }
    public string? Author { get; private init; }
    public IReadOnlyList<string> Main { get; private init; } = [];
    public IReadOnlyList<string> Full { get; private init; } = [];
    public string Surname { get; private init; } = "";
    public string AuthorKey { get; private init; } = "";
    public IReadOnlyList<string> Characters { get; private init; } = [];
    public IReadOnlyList<TitleVariant> Alternates { get; private init; } = [];

    public bool HasTitle => Main.Count > 0;

    public static NormalizedHypothesis? From(QueryHypothesis? h)
    {
        if (h is null) return null;
        var title = Blank(h.Title) ? null : h.Title;
        var author = Blank(h.Author) ? null : h.Author;
        var sets = title is null ? null : TitleText.Sets(title);
        var titleKey = TitleText.Key(title ?? "");
        var alternates = new List<TitleVariant>();
        // Original title first, then the English title; a variant equal to the title (by key) adds nothing.
        foreach (var (isOriginal, value) in new[] { (true, h.OriginalTitle), (false, h.EnglishTitle) })
        {
            if (Blank(value) || TitleText.Key(value!) == titleKey) continue;
            var v = TitleText.Sets(value!);
            if (v.Main.Count > 0) alternates.Add(new TitleVariant(isOriginal, value!, v.Main, v.Full));
        }

        IReadOnlyList<string> main = sets?.Main ?? [], full = sets?.Full ?? [];
        if (main.Count == 0 && alternates.Count > 0)
        {
            // No usable title but an English/original one: promote it.
            var promoted = alternates[0];
            alternates.RemoveAt(0);
            title = promoted.Title;
            main = promoted.Main;
            full = promoted.Full;
        }

        return new NormalizedHypothesis
        {
            Title = title,
            Author = author,
            Main = main,
            Full = full,
            Surname = author is null ? "" : PersonNames.Surname(author),
            AuthorKey = author is null ? "" : PersonNames.Key(author),
            Characters = h.Characters.Where(c => !Blank(c)).ToList(),
            Alternates = alternates,
        };
    }

    /// <summary>
    /// The interpreted title is grounded in what the user typed: some typed non-author word supports it (typos allowed,
    /// "tee"/"tea"), is part of an interpreted character name, or is the title's initials ("lotr"); or the query asks for
    /// an installment ("book 2", "third"). An ungrounded title may rank but never auto-matches.
    /// </summary>
    public bool IsGroundedIn(CleanedQuery query)
    {
        if (!HasTitle) return true;
        var typed = Numerals.ToDigits(query.Search);
        var words = new HashSet<string>(Full, StringComparer.Ordinal);
        words.UnionWith(TextFolding.Tokens(Title));
        foreach (var a in Alternates)
        {
            words.UnionWith(a.Full);
            words.UnionWith(TextFolding.Tokens(a.Title));
        }
        var characterTokens = CharacterTokens();
        var mainTokens = TextFolding.Tokens(TitleText.Split(Title!).Main).SkipWhile(t => t is "the" or "a" or "an");
        var initials = string.Concat(mainTokens.Select(t => t[0]));
        foreach (var t in typed)
        {
            if (Surname.Length > 0 && TokenMatch.Equal(t, Surname)) continue;
            if (words.Any(w => TokenMatch.Supports(t, w)) || characterTokens.Contains(t) || (t.Len() >= 3 && t == initials)) return true;
        }
        return Vocabulary.HasOrdinal(query);
    }

    /// <summary>Folded tokens of every LLM character name.</summary>
    public HashSet<string> CharacterTokens()
        => Characters.SelectMany(c => TextFolding.Tokens(c)).ToHashSet(StringComparer.Ordinal);

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
}
