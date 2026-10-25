using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Search;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Contracts;

/// <summary>
/// <see cref="SearchOutcome"/> → the HTTP contract. Pure: no I/O, so it is unit-tested with hand-built outcomes.
/// Display caps live here (Core keeps every author, so no matching evidence is lost); the matched name is always kept.
/// </summary>
public sealed class SearchResponseMapper(IOptions<OpenLibraryOptions> openLibrary, SearchOptions search, IAiPricing? pricing = null)
{
    public const int MaxPrimaryAuthors = 3;
    public const int MaxContributors = 3;
    public const int MaxMatchedSubjects = 3;

    private readonly OpenLibraryOptions _links = openLibrary.Value;

    public SearchResponse Map(SearchOutcome outcome, string requestId) => new()
    {
        Query = outcome.Query.Raw,
        Status = outcome.Status,
        StatusReason = outcome.Reason,
        Hint = outcome.Hint,
        Degraded = outcome.Degraded.Count > 0,
        DegradedReasons = outcome.Degraded,
        Interpretation = MapInterpretation(outcome),
        Candidates = [.. outcome.Items.Select(MapCandidate)],
        Meta = new SearchMeta
        {
            RequestId = requestId,
            ElapsedMs = outcome.Trace.ElapsedMs,
            TimingsMs = outcome.Trace.TimingsMs,
            OpenLibrary = MapCatalog(outcome.Trace),
            Llm = MapLlm(outcome.Trace.LlmCalls),
            Judge = MapJudge(outcome.Judge),
        },
    };

    private static SearchInterpretation MapInterpretation(SearchOutcome o)
    {
        var h = o.Hypothesis;
        return new SearchInterpretation
        {
            Title = h?.Title,
            Author = h?.Author,
            Characters = h?.Characters ?? [],
            // The heuristic arm has no parse of its own: what it searches with are the cleaned content words.
            Keywords = h?.Keywords ?? o.Query.Search,
            EnglishTitle = h?.EnglishTitle,
            OriginalTitle = h?.OriginalTitle,
            Year = o.Query.Year,
            RemovedNoise = o.Query.RemovedNoise,
            Source = o.InterpretationSource,
            // What the pipeline used, not what the LLM said: the heuristic "book about …" frame counts too.
            Intent = o.Topic is null ? QueryIntent.Book : QueryIntent.Topic,
            Topics = o.Topic?.Terms ?? [],
        };
    }

    public SearchCandidate MapCandidate(SearchItem item)
    {
        var ranked = item.Work;
        var work = ranked.Work;
        var evidence = ranked.Evidence;
        var primary = KeepMatched(ranked.PrimaryAuthors, MaxPrimaryAuthors, evidence.Role == AuthorRole.Primary ? evidence.MatchedAuthor : null);
        var contributors = KeepMatched(ranked.Contributors, MaxContributors, evidence.Role == AuthorRole.Contributor ? evidence.MatchedAuthor : null);
        return new SearchCandidate
        {
            Rank = item.Rank,
            WorkId = work.WorkId,
            Title = work.Title,
            Subtitle = work.Subtitle,
            Author = AuthorLine(primary, ranked.PrimaryAuthors.Count),
            PrimaryAuthors = [.. primary.Select(a => new AuthorLink { Key = a.Key, Name = a.Name, Url = AuthorUrl(a.Key) })],
            PrimaryAuthorsTotal = ranked.PrimaryAuthors.Count,
            Contributors = [.. contributors.Select(a => new ContributorLink { Key = a.Key, Name = a.Name, Role = a.Role })],
            ContributorsTotal = ranked.Contributors.Count,
            FirstPublishYear = work.FirstPublishYear is > 0 ? work.FirstPublishYear : null, // an implausible year (≤ 0) is unknown
            EditionCount = work.EditionCount,
            // An orphan edition id (OL…M) links to /books/ (DA-059); a work opens on the edition the search matched.
            OpenLibraryUrl = OpenLibraryLinks.Work(_links, work.WorkId, work.EditionKey), // /books/ for an orphan edition (DA-059); opens on the matched edition
            CoverUrl = OpenLibraryLinks.Cover(_links, work.CoverId),
            MatchTier = ranked.Tier,
            MatchTierLabel = TierLabel(ranked.Tier, evidence),
            MatchedSubjects = [.. evidence.MatchedSubjects.Take(MaxMatchedSubjects)],
            Explanation = item.Explanation.Text,
            ExplanationSource = item.Explanation.Source,
            SecondOpinion = item.SecondOpinion,
            DuplicatesFolded = ranked.FoldedWorkIds.Count,
        };
    }

    public static string TierLabel(MatchTier tier, MatchEvidence evidence) => tier switch
    {
        MatchTier.A when evidence.Role is null => "Exact title (no author given)",
        MatchTier.A => "Exact title + primary author",
        MatchTier.B => "Exact title + contributor-only author",
        MatchTier.C => "Near-match title + author",
        MatchTier.D => "Author only",
        _ => "No clear winner",
    };

    /// <summary>The first <paramref name="cap"/> credits, swapping the last slot for the matched name if it fell outside.</summary>
    public static IReadOnlyList<AuthorCredit> KeepMatched(IReadOnlyList<AuthorCredit> all, int cap, string? matched)
    {
        if (all.Count <= cap) return all;
        var shown = all.Take(cap).ToList();
        if (matched is not null && !shown.Any(a => SameName(a.Name, matched))
            && all.FirstOrDefault(a => SameName(a.Name, matched)) is { } hit)
        {
            shown[cap - 1] = hit;
        }
        return shown;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? AuthorLine(IReadOnlyList<AuthorCredit> shown, int total)
    {
        if (shown.Count == 0) return null;
        var names = string.Join(", ", shown.Select(a => a.Name));
        return total > shown.Count ? $"{names} and {total - shown.Count} more" : names;
    }

    private string? AuthorUrl(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"{_links.PublicUrl.TrimEnd('/')}/authors/{Uri.EscapeDataString(key)}";

    private static OpenLibraryMeta MapCatalog(SearchTrace trace)
    {
        var calls = trace.CatalogCalls;
        return new OpenLibraryMeta
        {
            Calls = calls.Count,
            CacheHits = calls.Count(c => c.CacheHit),
            UpstreamRequests = calls.Sum(c => c.Attempts),
            // The slow-empty re-query is one more attempt inside the same call, but it is not a retry.
            Retries = calls.Sum(c => Math.Max(0, c.Attempts - 1 - (c.SlowEmptyRetried ? 1 : 0))),
            SlowEmptyRequeries = calls.Count(c => c.SlowEmptyRetried),
            Budget = trace.CatalogCallBudget,
            Steps = [.. calls.Select(c => new CatalogStep
            {
                Step = c.Step,
                Outcome = c.Outcome,
                CacheHit = c.CacheHit,
                Attempts = c.Attempts,
                ElapsedMs = c.ElapsedMs,
                Results = c.Results,
                NumFound = c.NumFound,
            })],
        };
    }

    private LlmMeta MapLlm(IReadOnlyList<LlmCall> calls)
    {
        // A provider request was made unless the answer came from the cache or the call was skipped before sending.
        var sent = calls.Where(c => !c.CacheHit && c.Status is not (LlmCallStatus.NotConfigured or LlmCallStatus.BudgetExhausted)).ToList();
        // A 429 is refused before any token is processed, so it costs nothing; any other request without usage is unknown.
        var billed = sent.Where(c => c.Status != LlmCallStatus.RateLimited).ToList();
        long? input = billed.Any(c => c.InputTokens is null) ? null : billed.Sum(c => c.InputTokens!.Value);
        long? output = billed.Any(c => c.OutputTokens is null) ? null : billed.Sum(c => c.OutputTokens!.Value);
        decimal? cost = 0m;
        foreach (var c in billed)
        {
            var estimate = c.InputTokens is null || c.OutputTokens is null ? null : pricing?.Estimate(c.Model, c.InputTokens, c.OutputTokens);
            cost = estimate is null ? null : cost + estimate;
            if (cost is null) break;
        }

        return new LlmMeta
        {
            Provider = Distinct(calls.Select(c => c.Provider)),
            Model = Distinct(calls.Select(c => c.Model)),
            Calls = sent.Count,
            CacheHits = calls.Count(c => c.CacheHit),
            InputTokens = input,
            OutputTokens = output,
            EstCostUsd = cost is { } total ? decimal.Round(total, 8) : null,
            PromptVersions = calls.GroupBy(c => c.Operation, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().PromptId, StringComparer.Ordinal),
            Steps = [.. calls.Select(c => new LlmStep
            {
                Operation = c.Operation,
                Provider = c.Provider,
                Model = c.Model,
                PromptId = c.PromptId,
                Status = c.Status,
                CacheHit = c.CacheHit,
                ElapsedMs = c.ElapsedMs,
                InputTokens = c.InputTokens,
                OutputTokens = c.OutputTokens,
            })],
        };
    }

    private JudgeMeta MapJudge(JudgeVerdict v)
    {
        var verdict = v.Status is JudgeStatus.Agreed or JudgeStatus.Disagreed or JudgeStatus.ChoseNone;
        var called = v.Status is not (JudgeStatus.Off or JudgeStatus.Skipped);
        // Decisions bills input tokens only (it reports no output tokens), so output is 0, not unknown.
        var cost = called && v.Model is not null && v.InputTokens is not null ? pricing?.Estimate(v.Model, v.InputTokens, 0) : null;
        return new JudgeMeta
        {
            Enabled = v.Status != JudgeStatus.Off, // the pipeline reports off whenever the judge is switched off
            Status = v.Status,
            Pick = v.Pick,
            Probability = v.Probability is { } p && double.IsFinite(p) ? p : null, // SY-30: NaN never reaches JSON
            Threshold = search.JudgeThreshold,
            Agreed = verdict ? v.Status == JudgeStatus.Agreed : null,
            Model = v.Model,
            LatencyMs = called ? v.ElapsedMs : null,
            InputTokens = v.InputTokens,
            EstCostUsd = cost is { } c ? decimal.Round(c, 8) : null,
            Detail = v.Detail,
        };
    }

    private static string? Distinct(IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }
}
