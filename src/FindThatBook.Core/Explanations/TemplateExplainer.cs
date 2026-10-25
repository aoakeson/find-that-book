using System.Globalization;

namespace FindThatBook.Core.Explanations;

/// <summary>
/// The deterministic "why this book" sentence, in the brief's style ("Exact title match; Tolkien is the primary author;
/// Dixon is listed as adapter."). Built only from <see cref="ExplanationFacts"/>, so it passes <see cref="GroundingCheck"/>
/// by construction (tested over generated facts). Used when the LLM is off, failed, or wrote an ungrounded sentence.
/// </summary>
public static class TemplateExplainer
{
    /// <summary>One sentence of at most <see cref="GroundingCheck.MaxLength"/> characters; optional clauses go first when it is too long.</summary>
    public static string Explain(ExplanationFacts facts)
    {
        string sentence = "";
        for (var detail = Detail.Full; detail >= Detail.Minimal; detail--)
        {
            sentence = Compose(facts, detail);
            if (sentence.Length <= GroundingCheck.MaxLength) return sentence;
        }
        return sentence;
    }

    /// <summary>
    /// How much to say, dropped one step at a time until the sentence fits: edition/duplicate counts, then other
    /// characters (the first one stays), then the year, then extra contributors, the edition title and the alias; Minimal
    /// keeps the match phrase, one primary surname, the matched contributor's surname with role and one listed character
    /// (all the grounding check requires).
    /// </summary>
    private enum Detail { Minimal, NoContributorExtras, NoYear, NoCharacters, NoCounts, Full }

    private static string Compose(ExplanationFacts f, Detail detail)
    {
        var shortNames = detail == Detail.Minimal;
        var clauses = new List<string> { Lead(f, detail) };
        if (f.AlsoKnownAs is { } aka && detail > Detail.NoContributorExtras) clauses.Add($"{f.AlsoKnownAsLabel ?? "also published as"} {aka}");
        if (f.Intent is { } intent && detail > Detail.Minimal)
            clauses.Add(intent == Model.EditionIntent.StudyGuide ? "matches the requested study guide" : "matches the requested adaptation");
        clauses.Add(PrimaryClause(f, shortNames, withAlias: detail > Detail.NoContributorExtras));
        clauses.AddRange(ContributorClauses(f, detail));
        if (f.Kind != MatchKind.Character && f.MatchedCharacters.Count > 0)
        {
            var characters = detail > Detail.NoCharacters ? f.MatchedCharacters : f.MatchedCharacters.Take(1).ToList();
            clauses.Add(characters.Count == 1 ? $"{characters[0]} is a listed character" : $"{Join(characters)} are listed characters");
        }
        if (f.FirstPublishYear is { } year && detail > Detail.NoYear)
            clauses.Add($"Open Library's first publication year {year} matches the year in the search");
        if (detail == Detail.Full && ShowsEditionCount(f.Kind))
            clauses.Add(f.EditionCount == 1 ? "1 edition" : $"{f.EditionCount.ToString("N0", CultureInfo.InvariantCulture)} editions");
        if (detail == Detail.Full && f.DuplicateRecords > 0)
            clauses.Add(f.DuplicateRecords == 1 ? "1 duplicate record merged" : $"{f.DuplicateRecords.ToString("N0", CultureInfo.InvariantCulture)} duplicate records merged");
        return string.Join("; ", clauses) + ".";
    }

    private static string Lead(ExplanationFacts f, Detail detail)
    {
        if (f.Kind == MatchKind.Character && f.MatchedCharacters.Count > 0)
            return $"{f.MatchPhrase} on {Join(f.MatchedCharacters)}";
        if (f.Kind == MatchKind.Topic && f.MatchedSubjects.Count > 0)
            return f.MatchedSubjects.Count == 1 || detail == Detail.Minimal
                ? $"{f.MatchPhrase}: Open Library lists the subject {f.MatchedSubjects[0]}"
                : $"{f.MatchPhrase}: Open Library lists the subjects {Join(f.MatchedSubjects)}";
        if (f.Kind is MatchKind.NearTitle or MatchKind.PartialTitle or MatchKind.InferredTitle && (f.MatchedTerms.Count > 0 && detail > Detail.Minimal))
            return f.MatchedTerms.Count == 1
                ? $"{f.MatchPhrase} on the word “{f.MatchedTerms[0]}”"
                : $"{f.MatchPhrase} on the words {Join(f.MatchedTerms.Select(t => $"“{t}”").ToList())}";
        return f.MatchPhrase;
    }

    private static string PrimaryClause(ExplanationFacts f, bool shortNames, bool withAlias)
    {
        if (f.PrimaryAuthors.Count == 0) return "Open Library lists no primary author";
        IReadOnlyList<string> names = shortNames ? [ExplanationText.Surname(f.PrimaryAuthors[0])] : f.PrimaryAuthors;
        var clause = names.Count == 1 ? $"{names[0]} is the primary author" : $"{Join(names)} are the primary authors";
        if (withAlias && !shortNames && f.AuthorAlias is { } alias && f.PrimaryAuthors.Count == 1 && ExplanationText.SameText(alias.Of, names[0]))
            clause += $", also listed as {alias.Alias}";
        return clause;
    }

    /// <summary>"Charles Dixon and Sean Deming are listed as adapters": one clause per role, in fact order.</summary>
    private static IEnumerable<string> ContributorClauses(ExplanationFacts f, Detail detail)
    {
        var contributors = detail > Detail.NoContributorExtras
            ? f.Contributors
            : f.Contributors.Where(c => f.MatchedContributor is not null && ExplanationText.SameText(c.Name, f.MatchedContributor)).ToList();
        foreach (var group in contributors.GroupBy(c => c.Role))
        {
            var names = group.Select(c => detail == Detail.Minimal ? ExplanationText.Surname(c.Name) : c.Name).ToList();
            yield return names.Count == 1
                ? $"{names[0]} is listed as {group.Key}"
                : $"{Join(names)} are listed as {RoleLexicon.Plural(group.Key)}";
        }
    }

    /// <summary>Edition counts explain the order of author-only and weak results (most-published first); for title matches they are noise.</summary>
    private static bool ShowsEditionCount(MatchKind kind) =>
        kind is MatchKind.AuthorOnly or MatchKind.ContributorOnly or MatchKind.Character or MatchKind.TitleOnly or MatchKind.PartialTitle or MatchKind.Weak
            or MatchKind.Topic or MatchKind.Suggested;

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
