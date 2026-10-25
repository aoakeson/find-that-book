using System.Text.RegularExpressions;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>The facts the retrieval ladder decides on, read from the final order.</summary>
    private static LadderSignals LadderSignalsFor(List<Candidate> ranked, RankingContext context, Mashup? mashup)
    {
        var top = ranked[0];
        var anyAtoC = ranked.Any(w => w.Tier is MatchTier.A or MatchTier.B or MatchTier.C);
        var authorOnly = !anyAtoC && ranked.Any(w => w.Tier == MatchTier.D && w.AuthorOnly && w.BySurname);
        var strong = (ranked.Count >= Weights.WeakPoolSize || authorOnly) && (top.Tier is MatchTier.A or MatchTier.B or MatchTier.C || authorOnly);
        var hasExactTier = ranked.Any(w => w.Tier is MatchTier.A or MatchTier.B);
        var (authorKey, authorName) = authorOnly ? PickTopWorksAuthor(ranked, context) : (null, null);

        // With no A/B work and no author-only reading, the leftover typed words (1–3 words of ≥ 3 letters, no digits,
        // minus the top's matched surnames) may be character names: the ladder retries them as a person search.
        IReadOnlyList<string> characterFallback = [];
        if (!hasExactTier && !authorOnly && (top.Tier is MatchTier.D or MatchTier.E || !top.BySurname))
        {
            var surnames = top.Role is null ? [] : top.AuthorKeys.Select(k => PersonNames.Surname(top.Author(k)!.Display)).ToList();
            var leftover = context.Search.Where(t => !surnames.Any(s => TokenMatch.Equal(t, s)) && !TextFolding.IsDigits(t) && t.Len() >= 3).ToList();
            if (leftover.Count is >= 1 and <= 3) characterFallback = leftover;
        }

        return new LadderSignals
        {
            Strong = strong,
            AuthorOnly = authorOnly,
            HasExactTier = hasExactTier,
            TopWorksAuthorKey = authorKey,
            AuthorName = authorName ?? (authorOnly ? AuthorOnlyName(ranked) : null),
            CharacterFallbackTokens = characterFallback,
            MashupProbeTokens = mashup?.ProbeTokens ?? [],
        };
    }

    /// <summary>
    /// For an author-only query: the matched primary author whose surname (or another ≥ 3-letter name word) is typed,
    /// preferring the interpreter's author, then a surname hit, then the most editions in the pool. Null when the
    /// interpreter named an author the pool does not hold (the ladder then searches by name).
    /// </summary>
    private static (string? Key, string? Name) PickTopWorksAuthor(List<Candidate> ranked, RankingContext context)
    {
        var hypothesis = context.Hypothesis;
        var tally = new Dictionary<string, (int Interpreted, int Surname, long Editions, string OlKey, string Display)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var row in ranked)
            foreach (var record in (IEnumerable<Candidate>)[row, .. row.Dups])
                foreach (var author in record.Authors)
                {
                    if (!record.Primary.Contains(author.NameKey)) continue;
                    var surname = PersonNames.Surname(author.Display);
                    var surnameTyped = context.Search.Any(t => TokenMatch.Equal(t, surname));
                    var nameTyped = context.Search.Any(t => t.Len() >= 3 && PersonNames.Tokens(author.Display).Any(n => TokenMatch.Equal(t, n)));
                    if (!(surnameTyped || nameTyped) || string.IsNullOrWhiteSpace(author.OlKey)) continue;
                    var interpreted = hypothesis is { AuthorKey.Length: > 0 } && (author.NameKey == hypothesis.AuthorKey
                        || (TokenMatch.Equal(hypothesis.Surname, surname) && FirstLetter(author.NameKey) == FirstLetter(hypothesis.AuthorKey))) ? 1 : 0;
                    if (!tally.TryGetValue(author.NameKey, out var t0))
                    {
                        t0 = (0, 0, 0, author.OlKey, author.Display);
                        order.Add(author.NameKey);
                    }
                    tally[author.NameKey] = (interpreted, Math.Max(t0.Surname, surnameTyped ? 1 : 0), t0.Editions + record.Editions, t0.OlKey, t0.Display);
                }
        if (order.Count == 0) return (null, null);
        var best = order[0];
        foreach (var key in order)
        {
            var (a, b) = (tally[key], tally[best]);
            if ((a.Interpreted, a.Surname, a.Editions).CompareTo((b.Interpreted, b.Surname, b.Editions)) > 0) best = key;
        }
        var chosen = tally[best];
        if (hypothesis is { AuthorKey.Length: > 0 } && chosen.Interpreted == 0) return (null, null);
        return (chosen.OlKey, chosen.Display);
    }

    private static string FirstLetter(string nameKey) => nameKey.Length == 0 ? "" : nameKey[..1];

    private static string? AuthorOnlyName(List<Candidate> ranked)
    {
        var work = ranked.FirstOrDefault(w => w.Tier == MatchTier.D && w.AuthorOnly && w.BySurname);
        var key = work?.AuthorKeys.FirstOrDefault(k => work.Primary.Contains(k)) ?? work?.AuthorKeys.FirstOrDefault();
        return key is null ? null : work!.Author(key)?.Display;
    }

    private static RankedWork ToRankedWork(Candidate work, RankingContext context)
    {
        var (primary, alsoCredited) = PrimaryCredits(work, context);
        var contributors = new List<AuthorCredit>();
        foreach (var author in work.Authors)
            if (work.Contributors.Contains(author.NameKey) && !ContributorRoles.IsPlaceholderName(author.Display))
                contributors.Add(new AuthorCredit(author.Display, author.OlKey, work.Roles.GetValueOrDefault(author.NameKey) ?? "contributor"));
        contributors.AddRange(alsoCredited);
        // Credited contributors who are not listed as authors ("Alan Lee (Illustrator)"), for explanations and the judge.
        var credited = work.Authors.Select(a => a.NameKey).ToHashSet(StringComparer.Ordinal);
        foreach (var credit in work.Work.Contributors)
        {
            var parsed = CreditPattern().Match(credit);
            if (!parsed.Success) continue;
            var role = parsed.Groups[2].Value.Trim().ToLowerInvariant();
            var key = PersonNames.Key(parsed.Groups[1].Value);
            if (key.Length == 0 || !ContributorRoles.IsContributorRole(role) || ContributorRoles.IsPlaceholderName(parsed.Groups[1].Value)
                || !credited.Add(key)) continue;
            contributors.Add(new AuthorCredit(parsed.Groups[1].Value.Trim(), null, role));
        }

        var matchedAuthor = work.AuthorKeys.Count == 0 ? null
            : work.Authors.FirstOrDefault(a => work.AuthorKeys.Contains(a.NameKey) && work.Primary.Contains(a.NameKey))
              ?? work.Authors.FirstOrDefault(a => work.AuthorKeys.Contains(a.NameKey));
        var shown = work.ShownTitle;
        var typedTitleWords = work.NonAuthorTokens.Concat(context.OptionalTitleWords.Where(t => shown.Tokens.Contains(t))).Distinct();
        IReadOnlyList<string> NameWords(string person) => PersonNames.Tokens(person).Where(t => t.Len() > 1).ToList();
        // A typo is allowed only on 5+ letters: on 4, real words collide ("frodo ring" named King Thranduil; DA-052).
        static bool CharacterWord(string typed, string name) => typed == name || (Math.Min(typed.Len(), name.Len()) >= 5 && TokenMatch.Equal(typed, name));

        return new RankedWork
        {
            Work = work.Work,
            Tier = work.Tier,
            Score = work.Score,
            PrimaryAuthors = primary,
            Contributors = contributors,
            FoldedWorkIds = [.. work.Dups.Select(d => d.Id)],
            Evidence = new MatchEvidence
            {
                ExactTitle = shown.Exact,
                TitleScore = shown.Score,
                TitleSource = shown.Source,
                MatchedTitleTerms = [.. typedTitleWords.Where(shown.Tokens.Contains)],
                Role = work.Role,
                MatchedAuthor = matchedAuthor?.Display,
                MatchedContributorRole = work.Role == AuthorRole.Contributor && matchedAuthor is not null
                    ? work.Roles.GetValueOrDefault(matchedAuthor.NameKey) : null,
                AuthorFromHypothesis = work.AuthorFromHypothesis,
                AuthorViaAlternativeName = work.ViaAlternativeName,
                CharacterCoverage = work.CharacterCoverage,
                MatchedCharacters = [.. work.Persons.Where(p => NameWords(p).Any(n => work.NonAuthorTokens.Any(t => CharacterWord(t, n)))).Distinct()],
                IntentMatch = work.IntentHit,
                YearMatched = work.YearMatched,
                MatchedEditionTitle = shown.EditionTitle,
                ForeignAuthor = work.Foreign,
                Penalties = [.. work.Penalties],
                MatchedSubjects = [.. work.MatchedSubjects],
                SuggestedByInterpreter = work.SuggestedByInterpreter,
            },
        };
    }

    /// <summary>
    /// Primary authors for display, in catalog order (inherited from the canonical when the work has none of its own).
    /// DA-026: a work that is its own canonical with ≥ 3 "primaries" (narrators, illustrators, real names) shows only the
    /// ones the query names, else those primary on another pool work, else the most-edited one; the rest are "also
    /// credited". Matching is unchanged.
    /// </summary>
    private static (List<AuthorCredit> Primary, List<AuthorCredit> AlsoCredited) PrimaryCredits(Candidate work, RankingContext context)
    {
        if (work.Primary.Count == 0 && work.InheritedFrom is { } canonical)
            return ([.. canonical.Authors.Where(a => canonical.Primary.Contains(a.NameKey)).Select(a => new AuthorCredit(a.Display, a.OlKey))], []);
        var primaries = work.Authors.Where(a => work.Primary.Contains(a.NameKey)).ToList();
        if (!work.SelfCanonical || primaries.Count < 3)
            return ([.. primaries.Select(a => new AuthorCredit(a.Display, a.OlKey))], []);
        var shown = primaries.Where(a => work.AuthorKeys.Contains(a.NameKey)).ToList();
        if (shown.Count == 0)
            shown = primaries.Where(a => context.Works.Any(o => o != work && o.Primary.Contains(a.NameKey))).ToList();
        if (shown.Count == 0)
        {
            WorkAuthor? mostEdited = null;
            long mostEditions = -1;
            foreach (var author in primaries)
            {
                var editions = context.Works.Where(o => o.Primary.Contains(author.NameKey)).Sum(o => (long)o.Editions);
                if (editions > mostEditions) { mostEdited = author; mostEditions = editions; }
            }
            shown = [mostEdited!];
        }
        return ([.. shown.Select(a => new AuthorCredit(a.Display, a.OlKey))],
                [.. primaries.Where(a => !shown.Contains(a)).Select(a => new AuthorCredit(a.Display, a.OlKey, "also credited"))]);
    }

    [GeneratedRegex(@"^(.*?)\s*\(([^)]*)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CreditPattern();
}
