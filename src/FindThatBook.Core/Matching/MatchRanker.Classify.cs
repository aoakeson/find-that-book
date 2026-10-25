using System.Text.RegularExpressions;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// Tier, evidence and score for one work. Tiers (the brief's matching hierarchy):
    /// A exact title + primary author, or an exact title when no author is named · B exact title + contributor only ·
    /// C near title + author · D author only · E anything else (partial title, keywords, characters).
    /// </summary>
    private static void Classify(Candidate work, RankingContext context)
    {
        var query = context.Query;
        var hypothesis = context.Hypothesis;
        var search = context.Search;

        // ---- authors ----
        var (typedAuthorTokens, matchedAuthorKeys) = MatchTypedAuthors(work, context);
        if (context.AuthorPhrase is { } phraseAuthor)
        {
            // The whole query is one author's full name: only that author counts.
            typedAuthorTokens = search.ToHashSet(StringComparer.Ordinal);
            matchedAuthorKeys = work.Author(phraseAuthor) is not null ? [phraseAuthor] : [];
            work.BySurname = true;
        }
        var nonAuthorTokens = search.Where(t => !typedAuthorTokens.Contains(t)).ToList();
        var characterNameTokens = work.Persons.SelectMany(PersonNames.Tokens).Where(t => t.Len() > 1).ToHashSet(StringComparer.Ordinal);
        work.CharacterCoverage = nonAuthorTokens.Count > 0 && characterNameTokens.Count > 0
            ? (double)nonAuthorTokens.Count(t => characterNameTokens.Any(p => TokenMatch.Equal(t, p))) / nonAuthorTokens.Count
            : 0.0;
        var unexplained = nonAuthorTokens.Where(t => !context.HypothesisCharacterTokens.Contains(t) && !work.Full.Any(f => TokenMatch.Equal(t, f))
                                                     && !characterNameTokens.Any(p => TokenMatch.Equal(t, p))).ToList();
        // The query names another author: an unexplained typed word is a pool author's surname ("hobbit dixon" for
        // Tolkien's own Hobbit), or names the pool's dominant author while this work is not theirs.
        var namesOtherAuthor = unexplained.Any(t => t.Len() >= 4 && context.PoolSurnames.Contains(t));
        var namesPoolSurname = namesOtherAuthor;
        if (context.DominantAuthorTokens.Count > 0 && search.Any(t => !typedAuthorTokens.Contains(t) && context.DominantAuthorTokens.Contains(t)))
            namesOtherAuthor = true;
        work.AuthorFromHypothesis = false;
        if (hypothesis is { Surname.Length: > 0 } && !namesOtherAuthor && matchedAuthorKeys.Count == 0)
        {
            // The interpreter's author counts only when the user typed no author of their own.
            foreach (var author in work.Authors)
                if (TokenMatch.Equal(hypothesis.Surname, PersonNames.Surname(author.Display)))
                {
                    matchedAuthorKeys.Add(author.NameKey);
                    work.AuthorFromHypothesis = true;
                }
            // R3-0366: the interpreter's author written another way (Maxim Gorky for a record credited "Максим Горький")
            // counts through the work's alternative names, as typed names do.
            if (matchedAuthorKeys.Count == 0 && context.Options.HypothesisAuthorCheck && hypothesis.Surname.Len() >= 4 && work.Authors.Count > 0
                && work.AlternativeNames.Any(n => IsPersonAlias(n) && TokenMatch.Equal(PersonNames.Surname(n), hypothesis.Surname)))
            {
                matchedAuthorKeys.Add(work.Authors[0].NameKey);
                work.AuthorFromHypothesis = true;
            }
        }
        var role = matchedAuthorKeys.Overlaps(work.Primary) ? AuthorRole.Primary
            : matchedAuthorKeys.Overlaps(work.Contributors) ? AuthorRole.Contributor
            : (AuthorRole?)null;

        // ---- title ----
        var title = MatchTitle(work, context, typedAuthorTokens, nonAuthorTokens);
        var exact = title.Best.Exact;
        var near = exact || (title.Best.Precision >= Weights.NearPrecision && title.Best.Recall >= Weights.NearRecall);
        var hypothesisAuthorTyped = hypothesis is { Surname.Length: > 0 } && search.Any(t => TokenMatch.Equal(hypothesis.Surname, t));

        // R3-0366: an exact title only the interpreter supplied, on a work by someone other than the interpreter's author,
        // is the wrong book (Tolstoy's Детство for Gorky's Childhood): treat it like a named other author.
        if (context.Options.HypothesisAuthorCheck && exact && !work.ExactTyped && role is null && hypothesis is { Surname.Length: > 0 }
            && work.Authors.Count > 0 && !AgreesWithAuthor(work, hypothesis))
            namesOtherAuthor = true;

        work.Foreign = namesOtherAuthor && matchedAuthorKeys.Count == 0;
        var queryNamesAnAuthor = typedAuthorTokens.Count > 0 || hypothesisAuthorTyped || namesOtherAuthor;
        work.AuthorOnly = role is not null && nonAuthorTokens.Count == 0;
        work.AuthorTokens = typedAuthorTokens;
        work.AuthorKeys = matchedAuthorKeys;
        work.Role = role;
        work.NonAuthorTokens = nonAuthorTokens;

        // ---- tier ----
        MatchTier tier;
        if (exact && (role == AuthorRole.Primary || (role is null && !queryNamesAnAuthor))) tier = MatchTier.A;
        else if (exact && role == AuthorRole.Contributor) tier = MatchTier.B;
        else if (near && role is not null) tier = MatchTier.C;
        else if (role is not null && (nonAuthorTokens.Count == 0 || title.Best.Score < 0.34 || !near)) tier = MatchTier.D;
        else tier = MatchTier.E;

        work.Penalties = WorkMarkers.Penalties(work.FoldedTitle, work.Authors.Select(a => TextFolding.Fold(a.Display)), query.All,
            !context.Options.StudyGuideOnly);
        work.VolumePart = context.Options.VolumeMarkers && WorkMarkers.HasVolumeMarker(work.FoldedTitle);
        if (work.VolumePart && context.TypedVolumeNumber is null) work.Penalties.Add(WorkMarkers.Volume);
        work.YearMatched = context.YearMatches(work);
        work.MatchedContributors = matchedAuthorKeys.Where(k => work.Contributors.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        // A typed surname credited on a same-title rival record, and not this work's own author or alias, is a named
        // contributor this work does not explain ("the hobbit tolkien dixon"): an exact primary match drops to C.
        work.NamedRivals = [];
        work.NamedTokens = [];
        if (namesPoolSurname)
        {
            var ownNameTokens = work.Authors.SelectMany(a => PersonNames.Tokens(a.Display))
                .Concat(work.AlternativeNames.SelectMany(PersonNames.Tokens)).ToHashSet(StringComparer.Ordinal);
            var sameTitleRecords = context.SurnamesBySameTitleRecord.GetValueOrDefault(work.TitleKey) ?? [];
            foreach (var t in unexplained)
            {
                if (t.Len() < 4 || ownNameTokens.Contains(t)) continue;
                foreach (var record in sameTitleRecords)
                    if (record.Key != work.Id && record.Value.Contains(t))
                    {
                        work.NamedRivals.Add(record.Key);
                        work.NamedTokens.Add(t);
                    }
            }
        }
        if (tier == MatchTier.A && role == AuthorRole.Primary && work.NamedRivals.Count > 0) tier = MatchTier.C;

        work.IntentHit = SatisfiesEditionIntent(work, query, hypothesis, nonAuthorTokens, !context.Options.StudyGuideOnly);
        work.Score = Score(work, title.Best.Score, role, namesOtherAuthor);
        work.Tier = tier;
        work.Exact = exact;
        work.TitleScore = title.Best.Score;
        work.TitlePrecision = title.Best.Precision;
        RecordTitleEvidence(work, query, title);
    }

    /// <summary>
    /// A work that satisfies the query's edition intent (graphic novel, study guide): it is of that kind (title, subtitle
    /// or an adapter credit) and the rest of the query, minus the intent and author words, still matches its title.
    /// </summary>
    private static bool SatisfiesEditionIntent(Candidate work, CleanedQuery query, NormalizedHypothesis? hypothesis, List<string> nonAuthorTokens,
        bool bareStudyWords)
    {
        if (query.Intents.Count == 0) return false;
        var kinds = WorkMarkers.Kinds(work.FoldedTitle, work.FoldedSubtitle, work.Authors.Select(a => TextFolding.Fold(a.Display)), work.Roles.Values,
            bareStudyWords);
        if (!query.Intents.Any(i => kinds.Contains(KindOf(i)))) return false;
        var rest = nonAuthorTokens.Where(t => !Vocabulary.AllIntentWords.Contains(t)).ToList();
        var forms = new List<IReadOnlyList<string>> { rest };
        if (hypothesis is { HasTitle: true }) forms.Add(hypothesis.Main);
        return (rest.Count == 0 && hypothesis is not { HasTitle: true })
               || forms.Any(v => TokenMatch.Score(v, work.Main).Precision >= Weights.NearPrecision
                                 || TokenMatch.Score(v, work.Full).Precision >= Weights.NearPrecision);
    }

    private static string KindOf(EditionIntent intent) => intent switch
    {
        EditionIntent.Adaptation => WorkMarkers.Adapt,
        EditionIntent.StudyGuide => WorkMarkers.Study,
        _ => WorkMarkers.Omnibus,
    };

    /// <summary>The best title match over every query variant × every title of the work (own title, subtitle, edition titles).</summary>
    private sealed record TitleMatchResult(TitleMatch Best, TitleSource Source, int TitleIndex, int VariantIndex,
        List<IReadOnlyList<string>> Titles, List<(IReadOnlyList<string> Tokens, TitleSource Source)> Variants);

    /// <summary>
    /// Query variants, in priority order: the typed words (a soft year is optional unless the title has it, IN-044; typed
    /// stop/noise words count when the title contains them), the words before a typed "or", then the interpreter's titles.
    /// Titles: the work's main title, main + subtitle, then each edition title the catalog matched (main, full).
    /// The best pair wins (exact first, then score); a typed title complete within one edit per long word is exact
    /// ("murdr on the orient express").
    /// </summary>
    private static TitleMatchResult MatchTitle(Candidate work, RankingContext context, HashSet<string> typedAuthorTokens, List<string> nonAuthorTokens)
    {
        var typedTitle = nonAuthorTokens.Where(t => !context.SoftTokens.Contains(t) || work.Full.Contains(t)).ToList();
        typedTitle.AddRange(context.OptionalTitleWords.Where(t => work.Full.Contains(t)));
        var variants = new List<(IReadOnlyList<string> Tokens, TitleSource Source)> { (typedTitle, TitleSource.Typed) };
        if (context.Query.Head.Count > 0) variants.Add((context.Query.Head.Where(t => !typedAuthorTokens.Contains(t)).ToList(), TitleSource.Typed));
        var typedVariantCount = variants.Count;
        variants.AddRange(context.HypothesisVariants);
        var titles = new List<IReadOnlyList<string>> { work.Main, work.Full };
        foreach (var edition in work.EditionSets)
        {
            titles.Add(edition.Main);
            titles.Add(edition.Full);
        }

        var best = TitleMatch.NoMatch;
        var bestSource = TitleSource.None;
        var bestTitle = -1;
        var bestVariant = -1;
        work.ExactTyped = false;
        for (var vi = 0; vi < variants.Count; vi++)
            for (var ti = 0; ti < titles.Count; ti++)
            {
                var score = TokenMatch.Score(variants[vi].Tokens, titles[ti]);
                if (score.Exact && vi < typedVariantCount) work.ExactTyped = true;
                if ((score.Exact && !best.Exact) || (score.Exact == best.Exact && score.Score > best.Score))
                    (best, bestSource, bestTitle, bestVariant) = (score, variants[vi].Source, ti, vi);
            }
        if (!best.Exact)
        {
            for (var vi = 0; vi < typedVariantCount && !best.Exact; vi++)
                for (var ti = 0; ti < titles.Count; ti++)
                {
                    if (!TokenMatch.FuzzyComplete(variants[vi].Tokens, titles[ti])) continue;
                    (best, bestSource, bestTitle, bestVariant) = (new TitleMatch(true, 1.0, 1.0, 1.0), TitleSource.Typed, ti, vi);
                    work.ExactTyped = true;
                    break;
                }
        }
        return new TitleMatchResult(best, bestSource, bestTitle, bestVariant, titles, variants);
    }

    /// <summary>
    /// The title facts explanations use: an editions.title match reports EditionTitle; otherwise the variant that matched
    /// first (typed before interpreted), so a plain typed title is never reported as an interpretation. Also notes when the
    /// match needed the work's subtitle (such a work keeps its own row when duplicates are folded).
    /// </summary>
    private static void RecordTitleEvidence(Candidate work, CleanedQuery query, TitleMatchResult title)
    {
        var titleIndex = title.TitleIndex;
        work.MatchedEditionTitle = titleIndex >= 2 ? work.EditionTitles[(titleIndex - 2) / 2] : null;
        work.TitleSource = work.MatchedEditionTitle is not null ? TitleSource.EditionTitle : title.Source;
        IReadOnlyList<string> matchedTitle = titleIndex >= 0 ? title.Titles[titleIndex] : work.Full;
        work.ShownTitle = new TitleEvidence(title.Best.Exact, title.Best.Score, work.TitleSource, work.MatchedEditionTitle,
            [.. work.Full, .. (titleIndex >= 2 ? matchedTitle : [])]);
        // Needed: the full title (odd index) beat the main title for the same query variant. An "or" subtitle is an
        // alternative title of the same work ("Frankenstein; or, The Modern Prometheus"), so it folds as usual.
        work.SubtitleKey = titleIndex >= 0 && titleIndex % 2 == 1 && title.Titles[titleIndex].Count > title.Titles[titleIndex - 1].Count
                           && Worse(TokenMatch.Score(title.Variants[title.VariantIndex].Tokens, title.Titles[titleIndex - 1]), title.Best)
                           && query.Head.Count == 0 && !IsOrTitle(SubtitleOf(work, titleIndex))
            ? string.Join(' ', title.Titles[titleIndex]) : "";
    }

    /// <summary>The folded subtitle behind title set <paramref name="index"/> (1 = the work's own, 3, 5… = its edition titles).</summary>
    private static string SubtitleOf(Candidate work, int index)
    {
        var title = index == 1 ? work.Work.Title : work.EditionTitles[(index - 3) / 2];
        var subtitle = TitleText.Split(title).Subtitle;
        return subtitle.Length > 0 || index != 1 ? subtitle : work.FoldedSubtitle;
    }

    private static bool IsOrTitle(string subtitle)
        => subtitle == "or" || subtitle.StartsWith("or ", StringComparison.Ordinal) || subtitle.StartsWith("or,", StringComparison.Ordinal);

    private static bool Worse(TitleMatch a, TitleMatch b) => (b.Exact && !a.Exact) || (a.Exact == b.Exact && a.Score < b.Score);

    /// <summary>The interpreter's author is one of this work's authors: same name key, same surname, or a usable alternative name with that surname.</summary>
    private static bool AgreesWithAuthor(Candidate work, NormalizedHypothesis hypothesis)
        => work.Authors.Any(a => a.NameKey == hypothesis.AuthorKey || TokenMatch.Equal(PersonNames.Surname(a.Display), hypothesis.Surname))
           || work.AlternativeNames.Any(n => IsPersonAlias(n) && TokenMatch.Equal(PersonNames.Surname(n), hypothesis.Surname));

    /// <summary>
    /// Typed words naming one of the work's authors → (those words, the authors' name keys); sets
    /// <see cref="Candidate.BySurname"/>. A word of ≥ 3 letters must fuzzily equal a name word, or the query word equals the
    /// author's joined initials ("jrr"). Alternative names (pen names, real names, other scripts) count too, credited to the
    /// first-listed author, when the typed name covers ≥ 2 words of one alternative name or its surname.
    /// </summary>
    private static (HashSet<string> Tokens, HashSet<string> Keys) MatchTypedAuthors(Candidate work, RankingContext context)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        work.BySurname = false;
        work.AliasTokens = [];
        work.ViaAlternativeName = false;
        foreach (var author in work.Authors)
        {
            var nameTokens = PersonNames.Tokens(author.Display);
            var initials = string.Concat(nameTokens.Where(t => t.Len() == 1));
            var nameWords = nameTokens.Where(t => t.Len() > 1).ToList();
            foreach (var typed in context.Search)
            {
                if ((typed.Len() >= 3 && nameWords.Any(t => TokenMatch.Equal(typed, t))) || (initials.Len() >= 2 && typed == initials))
                {
                    tokens.Add(typed);
                    keys.Add(author.NameKey);
                    if (nameWords.Count > 0 && TokenMatch.Equal(typed, nameWords[^1])) work.BySurname = true;
                }
            }
        }
        if (work.AlternativeNames.Count > 0 && work.Authors.Count > 0)
        {
            var firstAuthor = work.Authors[0].NameKey;
            foreach (var alias in work.AlternativeNames)
            {
                if (!IsPersonAlias(alias)) continue;
                var aliasWords = PersonNames.Tokens(alias).Where(t => t.Len() > 1).ToList();
                var hits = context.Search.Where(t => t.Len() >= 3 && !work.Full.Contains(t) && aliasWords.Any(w => TokenMatch.Equal(t, w))).ToList();
                if (hits.Distinct().Count() >= 2 || hits.Any(t => TokenMatch.Equal(t, aliasWords[^1]) && t.Len() >= 4))
                {
                    if (keys.Count == 0) work.ViaAlternativeName = true;
                    tokens.UnionWith(hits);
                    keys.Add(firstAuthor);
                    work.AliasTokens.UnionWith(hits);
                    if (hits.Any(t => TokenMatch.Equal(t, aliasWords[^1]))) work.BySurname = true;
                }
            }
        }
        return (tokens, keys);
    }

    /// <summary>A usable alternative name: no catalog junk ("Dickens, Charles (adapted by …)", "… and Others"), 2–4 name words.</summary>
    private static bool IsPersonAlias(string name)
        => AliasCharactersOk().IsMatch(name) && name.Count(ch => ch == ',') <= 1 && !AliasJunkWords().IsMatch(name)
           && PersonNames.Tokens(name).Count(t => t.Len() > 1) is >= 2 and <= 4;

    [GeneratedRegex(@"^[^\d()\[\]&/;:""]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasCharactersOk();

    [GeneratedRegex(@"\b(and|by|ed|edited|adapted|works|others)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AliasJunkWords();
}
