namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// Who is a primary author and who a contributor, per work. Author order on Open Library carries no meaning, so:
    /// <list type="bullet">
    /// <item>A work's canonical is the same-title work (same title key) that shares an author and has more editions; the
    /// work's primaries are the authors it shares with that canonical, minus anyone labelled as a contributor
    /// ("Charles Dixon (Adapter)"). A work with no such canonical keeps every unlabelled author. An explicit contributor
    /// never becomes primary solely because the record has no other listed author.</item>
    /// <item>Inside a same-title group, an author who is a contributor on any member and not a primary of the group's
    /// canonical becomes a contributor everywhere in the group; a work left with no primary inherits the canonical's
    /// (a "The Hobbit" record listing only Charles Dixon → Dixon contributor, Tolkien inherited).</item>
    /// </list>
    /// </summary>
    private static void ResolveRoles(List<Candidate> works, RankerOptions options)
    {
        var byTitleKey = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var titleKeyOrder = new List<string>();
        foreach (var work in works)
        {
            if (!byTitleKey.TryGetValue(work.TitleKey, out var group))
            {
                byTitleKey[work.TitleKey] = group = [];
                titleKeyOrder.Add(work.TitleKey);
            }
            group.Add(work);
        }

        foreach (var work in works)
        {
            work.Roles = ContributorRoles.Parse(work.Work.Contributors);
            var authors = work.Authors.Select(a => a.NameKey).ToList();
            // DA-006: a co-author who also narrated the audiobook stays an author (a performer credit never demotes one of ≤ 3 authors).
            var keepPerformers = options.PerformerRoles && authors.Count <= 3;
            var labelledContributors = authors
                .Where(k => ContributorRoles.IsContributorRole(work.Roles.GetValueOrDefault(k))
                            && !(keepPerformers && ContributorRoles.IsPerformer(work.Roles[k])))
                .ToHashSet(StringComparer.Ordinal);
            var largerPeers = byTitleKey[work.TitleKey].Where(o => o != work && o.SharesAuthorWith(work) && o.Editions > work.Editions).ToList();
            work.SelfCanonical = largerPeers.Count == 0;
            if (largerPeers.Count > 0)
            {
                var canonical = Candidate.MostEdited(largerPeers);
                var shared = authors.Where(k => canonical.Author(k) is not null).ToHashSet(StringComparer.Ordinal);
                var primary = shared.Where(k => !labelledContributors.Contains(k)).ToHashSet(StringComparer.Ordinal);
                work.Primary = primary.Count > 0 || (options.ExplicitContributorRole && labelledContributors.Count > 0) ? primary : shared;
            }
            else
            {
                var primary = authors.Where(k => !labelledContributors.Contains(k)).ToHashSet(StringComparer.Ordinal);
                work.Primary = primary.Count > 0 || (options.ExplicitContributorRole && labelledContributors.Count > 0) ? primary : authors.ToHashSet(StringComparer.Ordinal);
            }
            work.Contributors = authors.Where(k => !work.Primary.Contains(k)).ToHashSet(StringComparer.Ordinal);
            work.InheritedFrom = null;
        }

        foreach (var titleKey in titleKeyOrder)
        {
            var group = byTitleKey[titleKey];
            if (group.Count < 2) continue;
            var canonical = Candidate.MostEdited(group);
            var contributorsInGroup = group.SelectMany(o => o.Contributors).ToHashSet(StringComparer.Ordinal);
            foreach (var work in group)
            {
                var demoted = work.Primary.Where(k => contributorsInGroup.Contains(k) && !canonical.Primary.Contains(k)).ToList();
                if (demoted.Count == 0) continue;
                work.Primary.ExceptWith(demoted);
                work.Contributors.UnionWith(demoted);
                if (work.Primary.Count == 0) work.InheritedFrom = canonical;
            }
        }
    }
}
