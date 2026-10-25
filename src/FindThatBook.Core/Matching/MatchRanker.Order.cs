using FindThatBook.Core.Model;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// Topic evidence first (topic mode only), edition-intent hits next, then score + the tier-B bonus, then edition
    /// count, then work id (ordinal).
    /// </summary>
    private static int Compare(Candidate a, Candidate b)
    {
        if (a.OrderGroup != b.OrderGroup) return a.OrderGroup.CompareTo(b.OrderGroup);
        if (a.IntentHit != b.IntentHit) return a.IntentHit ? -1 : 1;
        var scoreA = a.Score + (a.Tier == MatchTier.B ? Weights.TierBBonus : 0.0);
        var scoreB = b.Score + (b.Tier == MatchTier.B ? Weights.TierBBonus : 0.0);
        if (scoreA != scoreB) return scoreB.CompareTo(scoreA);
        if (a.Editions != b.Editions) return b.Editions.CompareTo(a.Editions);
        return string.CompareOrdinal(a.Id, b.Id);
    }

    private static List<Candidate> Order(IEnumerable<Candidate> works)
    {
        var list = works.ToList();
        list.Sort(Compare); // a total order (ids are unique), so an unstable sort is still deterministic
        return list;
    }

    /// <summary>
    /// Folds duplicate records of one work into a single row. Group key: the title key (or that of a larger same-author
    /// record whose edition title this record's title is: "Vegetarian" → 채식주의자), the primary authors (or inherited
    /// ones), the typed contributors it matched, whether it satisfies the edition intent, a volume-part marker (IN-035)
    /// and, when the title match needed the subtitle, the full title ("… : The Two Towers" stays its own row). The
    /// representative is the most-edited member; it takes the group's best tier and, when that differs from its own, the
    /// title evidence that earned it, so explanations and tier agree. In topic mode the representative is the most-edited
    /// member with topic evidence (a larger record without subjects must not replace the one that has them), and it
    /// carries the group's topic evidence and best topic score.
    /// </summary>
    private static List<Candidate> FoldDuplicates(List<Candidate> ranked, RankerOptions options, bool topicMode = false)
    {
        var groupTitleKey = ranked.ToDictionary(w => w.Id, w => w.TitleKey, StringComparer.Ordinal);
        var byEditionTitleKey = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        foreach (var work in ranked)
            foreach (var key in work.EditionKeys)
            {
                if (!byEditionTitleKey.TryGetValue(key, out var list)) byEditionTitleKey[key] = list = [];
                list.Add(work);
            }
        foreach (var work in ranked)
        {
            if (!byEditionTitleKey.TryGetValue(work.TitleKey, out var list)) continue;
            Candidate? larger = null;
            foreach (var other in list)
                if (other != work && other.Editions > work.Editions && other.SharesAuthorWith(work) && (larger is null || other.Editions > larger.Editions))
                    larger = other;
            if (larger is not null) groupTitleKey[work.Id] = larger.TitleKey;
        }

        var leaders = new List<Candidate>();
        var leaderByKey = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var work in ranked)
        {
            var key = string.Join('\u0001', groupTitleKey[work.Id],
                string.Join('\u0002', work.PrimaryOrInherited.OrderBy(k => k, StringComparer.Ordinal)),
                string.Join('\u0002', work.MatchedContributors), work.IntentHit ? "1" : "0",
                work.VolumePart ? work.FoldedTitle : "",
                options.SubtitleWorks ? work.SubtitleKey : "");
            if (leaderByKey.TryGetValue(key, out var leader))
            {
                leader.Dups.Add(work);
                continue;
            }
            work.Dups = [];
            leaderByKey[key] = work;
            leaders.Add(work);
        }
        for (var i = 0; i < leaders.Count; i++)
        {
            var leader = leaders[i];
            if (leader.Dups.Count == 0) continue;
            List<Candidate> members = [leader, .. leader.Dups];
            var backed = topicMode ? members.Where(HasTopicEvidence).ToList() : [];
            var representative = Candidate.MostEdited(backed.Count > 0 ? backed : members);
            if (backed.Count > 0) CarryTopicEvidence(representative, backed);
            if (representative == leader) continue;
            if (options.SubtitleWorks && representative.Tier != leader.Tier)
            {
                // A leader with another title is, for this work, one of its titles.
                var otherTitle = leader.TitleKey != representative.TitleKey;
                representative.ShownTitle = leader.ShownTitle with
                {
                    Source = otherTitle && leader.ShownTitle.EditionTitle is null ? TitleSource.EditionTitle : leader.ShownTitle.Source,
                    EditionTitle = leader.ShownTitle.EditionTitle ?? (otherTitle ? leader.Work.Title : null),
                };
            }
            representative.Tier = leader.Tier;
            representative.Dups = [leader, .. leader.Dups.Where(d => d != representative)];
            leaders[i] = representative;
        }
        return Order(leaders);
    }

    /// <summary>Duplicate records are one work: its matched subjects, suggestion and best topic score are the group's.</summary>
    private static void CarryTopicEvidence(Candidate representative, List<Candidate> backed)
    {
        foreach (var member in backed)
        {
            if (member == representative) continue;
            foreach (var subject in member.MatchedSubjects)
                if (representative.MatchedSubjects.Count < 5 && !representative.MatchedSubjects.Contains(subject, StringComparer.OrdinalIgnoreCase))
                    representative.MatchedSubjects.Add(subject);
            representative.SubjectCoverage = Math.Max(representative.SubjectCoverage, member.SubjectCoverage);
            representative.SuggestedByInterpreter |= member.SuggestedByInterpreter;
            representative.Score = Math.Max(representative.Score, member.Score);
        }
    }

    /// <summary>
    /// R3-0390: after a weak-pool OR search, popular works sharing one common word ("A Clockwork Orange": tier E, no author,
    /// no near title) outscored the exact title by the author the user typed. An exact title with a typed author ranks
    /// above every tier-E work.
    /// </summary>
    private static void LiftExactTitlesByTypedAuthors(List<Candidate> ranked)
    {
        var firstE = ranked.FindIndex(w => w.Tier == MatchTier.E);
        if (firstE < 0) return;
        var lifted = ranked.Skip(firstE).Where(w => w.Tier is MatchTier.A or MatchTier.B && w.Role is not null && w.AuthorTokens.Count > 0).ToList();
        foreach (var work in lifted) ranked.Remove(work);
        ranked.InsertRange(firstE, lifted);
    }

    /// <summary>
    /// IN-050 ("it", no interpreter): *Five Children and It* (950 editions, tier E, one of three title words) outscored
    /// King's *It* (exact title). An exact-title work moves above a tier-E work directly above it that only partly matches
    /// the title, when that is a different work (no shared author, or its title lacks a typed word: "Gardening: Herbs" for
    /// "gardening trees") and the exact one is not tiny next to it (≥ 10% of its editions). A same-author title holding
    /// every typed word keeps the score order: a 78-edition "Tom Sawyer" record stays below *The Adventures of Tom
    /// Sawyer*. Character evidence (person-search hits) is never passed.
    /// </summary>
    private static void LiftExactTitlesOverPartialTitles(List<Candidate> ranked)
    {
        for (var j = 1; j < ranked.Count; j++)
        {
            var exactWork = ranked[j];
            if (exactWork.Tier is not (MatchTier.A or MatchTier.B) || !exactWork.Exact) continue;
            var target = j;
            while (target > 0 && ranked[target - 1] is { Tier: MatchTier.E, Exact: false, TitleScore: > 0 } partial
                   && partial.CharacterCoverage < 0.5
                   && (!partial.SharesAuthorWith(exactWork) || partial.TitlePrecision < 1.0)
                   && exactWork.Editions * Weights.TinyFactor >= partial.Editions)
                target--;
            if (target == j) continue;
            ranked.RemoveAt(j);
            ranked.Insert(target, exactWork);
        }
    }

    /// <summary>
    /// Golden contrib-03 ("the hobbit tolkien dixon"): Tolkien's own Hobbit leaves the typed name "dixon" unexplained, and
    /// Dixon is credited on a same-title record (the graphic novel). A work leaving a typed contributor unexplained never
    /// outranks the exact-title record that explains it, and, like a work by another named author, it cannot make the
    /// status ambiguous by outscoring that record.
    /// </summary>
    private static void LiftWorksNamingTypedContributors(List<Candidate> ranked)
    {
        for (var i = 0; i < ranked.Count; i++)
        {
            var work = ranked[i];
            if (work.NamedRivals.Count == 0) continue;
            bool Explains(Candidate rival) => rival != work && rival.Exact && work.NamedTokens.All(rival.AuthorTokens.Contains)
                                              && (work.NamedRivals.Contains(rival.Id) || rival.Dups.Any(d => work.NamedRivals.Contains(d.Id)));
            if (!ranked.Any(Explains)) continue;
            work.ExcludedByContributor = true;
            var below = ranked.FindIndex(i + 1, rival => Explains(rival));
            if (below < 0) continue; // the record that explains it already ranks above
            var explaining = ranked[below];
            ranked.RemoveAt(below);
            ranked.Insert(i, explaining);
            i++; // work moved to i + 1
        }
    }

    /// <summary>
    /// DA-009 (tiny records): a ≤ 2-edition #1 gives way to a same-title (or edition-title) work with ≥ 10× its editions.
    /// Not to a work the query ruled out: one leaving a typed contributor unexplained, one lacking the edition intent the
    /// top satisfies, or one by somebody other than the author the user typed ("alice beacon"; a pen-name record still
    /// gives way when the larger work carries that name as an alias).
    /// </summary>
    private static void PromoteLargerSameTitlePeer(List<Candidate> ranked, RankerOptions options)
    {
        if (ranked.Count < 2) return;
        var top = ranked[0];
        if (top.Editions > Weights.TinyMaxEditions) return;
        var typedAuthor = top.Role is not null && top.AuthorTokens.Count > 0 && !top.AuthorFromHypothesis;
        Candidate? peer = null;
        for (var i = 1; i < ranked.Count; i++)
        {
            var other = ranked[i];
            if ((options.NamedContributor && other.ExcludedByContributor) || (options.IntentRivals && top.IntentHit && !other.IntentHit)) continue;
            if (options.TypedAuthorTiny && typedAuthor && !(other.AuthorKeys.Overlaps(top.AuthorKeys) || other.AuthorTokens.Overlaps(top.AuthorTokens))) continue;
            if ((other.TitleKey == top.TitleKey || other.EditionKeys.Contains(top.TitleKey))
                && other.Editions >= Weights.TinyFactor * Math.Max(top.Editions, 1)
                && (peer is null || other.Editions > peer.Editions))
                peer = other;
        }
        if (peer is null) return;
        ranked.Remove(peer);
        ranked.Insert(0, peer);
    }
}
