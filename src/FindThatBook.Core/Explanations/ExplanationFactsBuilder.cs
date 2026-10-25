using FindThatBook.Core.Model;

namespace FindThatBook.Core.Explanations;

/// <summary>
/// RankedWork → <see cref="ExplanationFacts"/>. The query and hypothesis are used only to derive facts in code
/// (which title words matched, whether an alternative author name was typed, which edition title is English);
/// their text is never copied into the facts, so the LLM never sees user text (AI-29/AI-30).
/// </summary>
public static class ExplanationFactsBuilder
{
    public const int MaxPrimaryAuthors = 3;
    public const int MaxContributors = 2;
    public const int MaxTitleLength = 120;
    public const int MaxAlsoKnownAsLength = 100;
    public const int MaxNameLength = 60;
    public const int MaxCharacters = 2;
    public const int MaxTerms = 5;
    public const int MaxSubjects = 2;
    public const int MaxSubjectLength = 50;
    public const int MaxSubjectWords = 6;

    /// <summary>Open Library's placeholder year on many classics (DA-061: 18 of 280 popular works).</summary>
    private const int SentinelYear = 1800;

    private static readonly HashSet<string> TermStopWords =
        ["the", "a", "an", "of", "and", "or", "in", "on", "to", "for", "by", "with", "at", "from", "de", "la", "le", "el", "les", "der", "die", "das"];

    public static string PhraseFor(MatchKind kind) => kind switch
    {
        MatchKind.ExactTitle => "Exact title match",
        MatchKind.InferredTitle => "Interpreted title match",
        MatchKind.OriginalTitle => "Original-language title match",
        MatchKind.NearTitle => "Near title match",
        MatchKind.AuthorOnly => "Author-only match",
        MatchKind.ContributorOnly => "Contributor-only match",
        MatchKind.Character => "Character match",
        MatchKind.TitleOnly => "Exact title, but not by the searched author",
        MatchKind.PartialTitle => "Partial title match",
        MatchKind.Topic => "Topic match",
        MatchKind.Suggested => "Suggested for this topic",
        _ => "Weak match",
    };

    public static IReadOnlyList<ExplanationFacts> Build(IReadOnlyList<RankedWork> works, CleanedQuery query, QueryHypothesis? hypothesis)
    {
        var facts = new List<ExplanationFacts>(works.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var work in works)
            if (seen.Add(work.Work.WorkId)) facts.Add(Build(work, query, hypothesis));
        return facts;
    }

    public static ExplanationFacts Build(RankedWork ranked, CleanedQuery query, QueryHypothesis? hypothesis)
    {
        var work = ranked.Work;
        var ev = ranked.Evidence;
        var title = ExplanationText.Cap(work.Title ?? "", MaxTitleLength);

        var primaries = DistinctNames(ranked.PrimaryAuthors.Select(a => a.Name)).Take(MaxPrimaryAuthors).ToList();
        var (contributors, matchedContributor) = Contributors(ranked, primaries);

        var aka = AlsoKnownAs(ranked, work.Title ?? "");
        var terms = MatchedTerms(ev, work.Title ?? "", aka);
        var characters = MatchedCharacters(ev, work);
        var subjects = MatchedSubjects(ev, TopicTokens(query, hypothesis));
        var kind = KindOf(ranked, matchedContributor, characters, terms, subjects);
        var year = ev.YearMatched && work.FirstPublishYear is { } y && y > 0 && y != SentinelYear ? y : (int?)null;

        return new ExplanationFacts
        {
            Id = work.WorkId,
            Title = title,
            Kind = kind,
            MatchPhrase = PhraseFor(kind),
            PrimaryAuthors = primaries,
            Contributors = contributors,
            MatchedContributor = matchedContributor,
            FirstPublishYear = year,
            EditionCount = Math.Max(0, work.EditionCount),
            MatchedTerms = terms,
            MatchedCharacters = characters,
            AlsoKnownAs = aka,
            AlsoKnownAsLabel = aka is null ? null : IsEnglishTitle(aka, work.Title ?? "", hypothesis) ? "known in English as" : "also published as",
            AuthorAlias = Alias(ranked, primaries, query),
            Intent = ev.IntentMatch ? query.Intents.Contains(EditionIntent.Adaptation) ? EditionIntent.Adaptation
                : query.Intents.Contains(EditionIntent.StudyGuide) ? EditionIntent.StudyGuide : null : null,
            DuplicateRecords = ranked.FoldedWorkIds.Count,
            MatchedSubjects = subjects,
        };
    }

    private static MatchKind KindOf(RankedWork ranked, string? matchedContributor, IReadOnlyList<string> characters, IReadOnlyList<string> terms,
        IReadOnlyList<string> subjects)
    {
        var ev = ranked.Evidence;
        // Topic requests: subject evidence is the reason for the result; a verified suggestion without it is only a
        // suggestion (unless the typed words also matched its title, which the tiers already say).
        if (subjects.Count > 0) return MatchKind.Topic;
        if (ev.SuggestedByInterpreter && ranked.Tier is MatchTier.D or MatchTier.E) return MatchKind.Suggested;
        return ranked.Tier switch
        {
            MatchTier.A or MatchTier.B => ev.TitleSource switch
            {
                TitleSource.Hypothesis => MatchKind.InferredTitle,
                TitleSource.OriginalTitle => MatchKind.OriginalTitle,
                _ => MatchKind.ExactTitle,
            },
            MatchTier.C => MatchKind.NearTitle,
            MatchTier.D => matchedContributor is not null ? MatchKind.ContributorOnly : MatchKind.AuthorOnly,
            _ when characters.Count > 0 => MatchKind.Character,
            _ when ev.ExactTitle => MatchKind.TitleOnly,
            _ when terms.Count > 0 || ev.TitleScore > 0 => MatchKind.PartialTitle,
            _ => MatchKind.Weak,
        };
    }

    /// <summary>
    /// Contributors only when they explain the match: the contributor the query named (tier B,
    /// contributor-only), then adapters on a tier A–C work (it is an adaptation, or the query asked for one). Narrators,
    /// translators and editors of some edition are noise, and author-only (D) or partial (E) results name none.
    /// </summary>
    private static (IReadOnlyList<ContributorFact> All, string? Matched) Contributors(RankedWork ranked, IReadOnlyList<string> primaries)
    {
        var known = new List<ContributorFact>();
        foreach (var c in ranked.Contributors)
        {
            var name = ExplanationText.Cap(ExplanationText.CleanName(c.Name), MaxNameLength);
            var role = RoleLexicon.Parse(c.Role);
            if (name.Length == 0 || role is null || RoleLexicon.IsJunkName(name) || known.Any(x => ExplanationText.SameText(x.Name, name))) continue;
            known.Add(new ContributorFact(name, role));
        }
        var adaptation = ranked.Tier is MatchTier.A or MatchTier.B or MatchTier.C;
        var list = known.Where(c => adaptation && c.Role == "adapter").ToList();

        string? matched = null;
        var ev = ranked.Evidence;
        if (ev.Role == AuthorRole.Contributor && ExplanationText.CleanName(ev.MatchedAuthor) is { Length: > 0 } who
            && !RoleLexicon.IsJunkName(who) && !primaries.Any(p => ExplanationText.SameText(p, who)))
        {
            list.RemoveAll(x => ExplanationText.SameText(x.Name, who));
            var existing = known.FindIndex(x => ExplanationText.SameText(x.Name, who));
            ContributorFact fact;
            if (existing >= 0)
            {
                fact = known[existing];
            }
            else
            {
                fact = new ContributorFact(ExplanationText.Cap(who, MaxNameLength), RoleLexicon.Parse(ev.MatchedContributorRole) ?? RoleLexicon.Generic);
            }
            list.Insert(0, fact);
            matched = fact.Name;
        }
        return (list.Take(MaxContributors).ToList(), matched);
    }

    /// <summary>
    /// The edition title, only when it is the title evidence of a tier A–C match (DE-06): never for author-only or weak
    /// results, where it is just some edition's title ("Tom Sawyer ilmailija…" on Tom Sawyer Abroad).
    /// </summary>
    private static string? AlsoKnownAs(RankedWork ranked, string title)
    {
        var ev = ranked.Evidence;
        if (ranked.Tier is not (MatchTier.A or MatchTier.B or MatchTier.C) || ev.TitleSource is not (TitleSource.EditionTitle or TitleSource.OriginalTitle)) return null;
        if (string.IsNullOrWhiteSpace(ev.MatchedEditionTitle) || ExplanationText.SameText(ev.MatchedEditionTitle, title)) return null;
        var aka = ExplanationText.Cap(ev.MatchedEditionTitle, MaxAlsoKnownAsLength);
        return ExplanationText.Skeleton(aka).Skeleton.Length == 0 ? null : aka;
    }

    /// <summary>
    /// "Known in English as" only when that is safe to say: the interpreter's English title names this edition title,
    /// or the work title is in another script and the edition title is Latin script (三体 → The Three-Body Problem).
    /// </summary>
    private static bool IsEnglishTitle(string aka, string title, QueryHypothesis? hypothesis) =>
        (hypothesis?.EnglishTitle is { } english && ExplanationText.SameText(english, aka))
        || (ExplanationText.HasNonLatinLetter(title) && ExplanationText.IsLatinOnly(aka));

    /// <summary>The matcher's typed title terms, kept only if they literally occur in the catalog title, so a fuzzy or injected token never reaches the facts.</summary>
    private static IReadOnlyList<string> MatchedTerms(MatchEvidence ev, string title, string? aka)
    {
        var catalog = new HashSet<string>(ExplanationText.Tokens(title), StringComparer.Ordinal);
        if (aka is not null) catalog.UnionWith(ExplanationText.Tokens(aka));
        return ev.MatchedTitleTerms
            .Select(ExplanationText.Fold)
            .Where(t => t.Length is > 0 and <= 30 && catalog.Contains(t) && !TermStopWords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTerms)
            .ToList();
    }

    /// <summary>Matched characters as Open Library names them: an evidence entry must be one of the work's persons, or all of its tokens must be in one.</summary>
    private static IReadOnlyList<string> MatchedCharacters(MatchEvidence ev, CandidateWork work)
    {
        var result = new List<string>();
        foreach (var m in ev.MatchedCharacters)
        {
            var tokens = ExplanationText.Tokens(m);
            if (tokens.Count == 0) continue;
            var person = work.Persons.FirstOrDefault(p => ExplanationText.SameText(p, m))
                ?? work.Persons.FirstOrDefault(p =>
                {
                    var pt = ExplanationText.Tokens(p);
                    return tokens.All(t => pt.Contains(t, StringComparer.Ordinal));
                });
            if (person is null) continue;
            var name = ExplanationText.Cap(ExplanationText.CleanName(person), MaxNameLength);
            if (name.Length == 0) continue;
            // One name per character: "Mr. Sherlock Holmes" and "Sherlock Holmes" are both listed; keep the shorter.
            var words = ExplanationText.Tokens(name);
            if (result.Any(r => ExplanationText.Tokens(r).All(words.Contains))) continue;
            result.RemoveAll(r => words.All(ExplanationText.Tokens(r).Contains));
            result.Add(name);
            if (result.Count == MaxCharacters) break;
        }
        return result;
    }

    /// <summary>
    /// Subject headings for a topic match, at most 2, quoted verbatim: the whole heading when it is short and plain
    /// ("Animals -- Bears"), else the subdivision that holds the topic word ("Bears"), never a part without it
    /// ("Animals"). Headings with markup, links or more than 6 words are dropped.
    /// </summary>
    private static IReadOnlyList<string> MatchedSubjects(MatchEvidence ev, IReadOnlySet<string> topic)
    {
        var kept = new List<string>();
        foreach (var raw in ev.MatchedSubjects)
        {
            var subject = Tidy(raw);
            if (!IsPlainSubject(subject) || (topic.Count > 0 && !MentionsTopic(subject, topic)))
            {
                subject = topic.Count == 0 ? "" : subject.Split("--").Select(Tidy).FirstOrDefault(p => IsPlainSubject(p) && MentionsTopic(p, topic)) ?? "";
            }
            // "Bears" adds nothing next to "Bears -- Juvenile fiction".
            if (subject.Length == 0 || kept.Any(k => Subdivisions(subject).All(Subdivisions(k).Contains))) continue;
            kept.Add(subject);
            if (kept.Count == MaxSubjects) break;
        }
        return kept;
    }

    private static HashSet<string> Subdivisions(string heading) =>
        heading.Split("--").Select(p => ExplanationText.Skeleton(p).Skeleton).Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static string Tidy(string? heading) =>
        string.Join(' ', (heading ?? "").Trim().TrimEnd('.', ',', ';', ':').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The topic words (the interpreter's subjects, else the cleaner's topic): used only to choose among catalog subdivisions.</summary>
    private static IReadOnlySet<string> TopicTokens(CleanedQuery query, QueryHypothesis? hypothesis)
    {
        var phrases = hypothesis is { Subjects.Count: > 0 } ? hypothesis.Subjects : query.Topic;
        return phrases.SelectMany(ExplanationText.Tokens).Where(t => t.Length > 2).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A whole-word, plural-tolerant hit ("bear" ~ "Bears", "fox" ~ "Foxes").</summary>
    private static bool MentionsTopic(string subject, IReadOnlySet<string> topic) =>
        ExplanationText.Tokens(subject).Any(w => topic.Any(t => w == t || w == t + "s" || t == w + "s" || w == t + "es" || t == w + "es"));

    private static bool IsPlainSubject(string subject) =>
        subject.Length is > 0 and <= MaxSubjectLength
        && ExplanationText.Tokens(subject).Count <= MaxSubjectWords
        && subject.Any(char.IsLetter)
        && subject.All(c => char.IsLetterOrDigit(c) || c is ' ' or ',' or '-' or '\'' or '’' or '(' or ')' or '&')
        && !ExplanationText.Tokens(subject).Any(t => t is "http" or "https" or "www" or "dot" or "com");

    /// <summary>DA-007: the alternative name of the matched primary author whose words the query typed ("richard bachman" → King).</summary>
    private static AuthorAliasFact? Alias(RankedWork ranked, IReadOnlyList<string> primaries, CleanedQuery query)
    {
        var ev = ranked.Evidence;
        if (!ev.AuthorViaAlternativeName || ev.Role != AuthorRole.Primary || ev.MatchedAuthor is null) return null;
        var of = primaries.FirstOrDefault(p => ExplanationText.SameText(p, ExplanationText.CleanName(ev.MatchedAuthor)));
        if (of is null) return null;
        var typed = new HashSet<string>(query.All.Select(ExplanationText.Fold), StringComparer.Ordinal);
        foreach (var alt in ranked.Work.AuthorAlternativeNames)
        {
            var name = ExplanationText.Cap(ExplanationText.CleanName(alt), MaxNameLength);
            var tokens = ExplanationText.Tokens(name).Where(t => t.Length > 1).ToList();
            if (tokens.Count > 0 && tokens.All(typed.Contains) && !ExplanationText.SameText(name, of))
                return new AuthorAliasFact(name, of);
        }
        return null;
    }

    private static IEnumerable<string> DistinctNames(IEnumerable<string?> names)
    {
        var kept = new List<string>();
        foreach (var raw in names)
        {
            var name = ExplanationText.Cap(ExplanationText.CleanName(raw), MaxNameLength);
            if (name.Length == 0 || kept.Any(k => ExplanationText.SameText(k, name))) continue;
            kept.Add(name);
            yield return name;
        }
    }
}
