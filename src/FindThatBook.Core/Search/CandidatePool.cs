using FindThatBook.Core.Model;

namespace FindThatBook.Core.Search;

/// <summary>
/// Works from every catalog call, keyed by work id. The first sighting keeps its position; a later call can only
/// add what the first lacked: character names (person:) or subject headings (topic calls). Merged in step order, so
/// the pool is deterministic.
/// </summary>
internal sealed class CandidatePool
{
    private readonly List<CandidateWork> _works = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public int Count => _works.Count;

    public IReadOnlyList<CandidateWork> Works => _works;

    public void Add(IEnumerable<CandidateWork> works)
    {
        foreach (var work in works)
        {
            if (!_index.TryGetValue(work.WorkId, out var i))
            {
                _index[work.WorkId] = _works.Count;
                _works.Add(work);
            }
            else
            {
                var known = _works[i];
                if (known.Persons.Count == 0 && work.Persons.Count > 0) known = known with { Persons = work.Persons };
                if (known.Subjects.Count == 0 && work.Subjects.Count > 0) known = known with { Subjects = work.Subjects };
                _works[i] = known;
            }
        }
    }
}
