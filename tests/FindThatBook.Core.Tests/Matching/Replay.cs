using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;
using FindThatBook.Core.Search;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// Runs the retrieval ladder + ranker over a fixture's recorded responses, in SearchService's order (broad, the
/// hypothesis calls, the 7b retry, then Next() one call at a time within the budget), without the async plumbing.
/// A step the recording lacks returns no works and is listed in <see cref="ReplayResult.Unrecorded"/>.
/// </summary>
internal static class Replay
{
    public static ReplayResult Run(PoolCase c, CleanedQuery query, RankerOptions options, int budget = 5)
    {
        var pool = new ReplayPool();
        var steps = new Dictionary<string, int?>(StringComparer.Ordinal);
        var issued = new List<CatalogRequest>();
        var unrecorded = new List<string>();

        IReadOnlyList<CandidateWork> Execute(CatalogRequest r)
        {
            issued.Add(r);
            // DE-05: a < 3-character query goes as title= (step short_title); the fixture records it as the broad call.
            var recorded = c.Calls.FirstOrDefault(x => x.Step == r.Step || (r.Step == CatalogSteps.ShortTitle && x.Step == CatalogSteps.Broad))
                           ?? c.ExtraCalls.FirstOrDefault(x => r.Query.Q is not null && x.Params.GetValueOrDefault("q") == r.Query.Q);
            if (recorded is null) unrecorded.Add(r.Step);
            var works = recorded?.Works ?? [];
            steps[r.Step] = works.Count;
            return works;
        }

        if (query.IsEmpty) return new ReplayResult(RankingResult.Empty, issued, unrecorded, []);
        var broad = Execute(CatalogQueries.Broad(query));
        var early = new List<IReadOnlyList<CandidateWork>>();
        foreach (var r in RetrievalLadder.AfterInterpretation(query, c.Hypothesis))
            if (!steps.ContainsKey(r.Step) && issued.Count < budget) early.Add(Execute(r));
        pool.Add(broad);
        foreach (var e in early) pool.Add(e);
        foreach (var r in RetrievalLadder.AfterBroad(query, c.Hypothesis, pool.Works, steps.Keys))
            if (!steps.ContainsKey(r.Step) && issued.Count < budget) pool.Add(Execute(r));

        var ranking = MatchRanker.Rank(query, c.Hypothesis, pool.Works, options);
        while (issued.Count < budget)
        {
            var next = RetrievalLadder.Next(new LadderState(query, c.Hypothesis, pool.Works, ranking,
                new Dictionary<string, int?>(steps), budget - issued.Count));
            if (next is null) break;
            steps.ContainsKey(next.Step).ShouldBeFalse($"the ladder re-issued {next.Step}");
            pool.Add(Execute(next));
            ranking = MatchRanker.Rank(query, c.Hypothesis, pool.Works, options);
        }
        return new ReplayResult(ranking, issued, unrecorded, [.. pool.Works]);
    }
}

internal sealed record ReplayResult(RankingResult Ranking, IReadOnlyList<CatalogRequest> Issued, IReadOnlyList<string> Unrecorded,
    IReadOnlyList<CandidateWork> Pool)
{
    public IReadOnlyList<string> Steps => [.. Issued.Select(r => r.Step)];
    public RankedWork Top => Ranking.Ranked[0];
    public IReadOnlyList<string> TopIds(int n) => [.. Ranking.Ranked.Take(n).Select(w => w.Work.WorkId)];
}

/// <summary>
/// SearchService's pool merge: step order, first sighting wins; a later call can add character names (person search) or
/// subject headings (topic search) to a work first seen without them.
/// </summary>
internal sealed class ReplayPool
{
    private readonly List<CandidateWork> _works = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<CandidateWork> Works => _works;

    public void Add(IEnumerable<CandidateWork> works)
    {
        foreach (var work in works)
        {
            if (!_index.TryGetValue(work.WorkId, out var i))
            {
                _index[work.WorkId] = _works.Count;
                _works.Add(work);
                continue;
            }
            if (_works[i].Persons.Count == 0 && work.Persons.Count > 0) _works[i] = _works[i] with { Persons = work.Persons };
            if (_works[i].Subjects.Count == 0 && work.Subjects.Count > 0) _works[i] = _works[i] with { Subjects = work.Subjects };
        }
    }
}
