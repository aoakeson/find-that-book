using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// R3-0195 ("Agatha Christie Murder on Nile Express …"): the interpreter picked one book (Death on the Nile) while the
    /// typed title words are split across two works by that author and no single work holds them all. The top keeps
    /// rank 1, the other reading goes to rank 2, the status is capped at ambiguous, and one OR search (author surname + the
    /// leftover title words) looks for the other work when the pool only shows it inside collections.
    /// </summary>
    private sealed class Mashup
    {
        private Mashup(Candidate? sibling, IReadOnlyList<string> probe)
        {
            Sibling = sibling;
            ProbeTokens = probe;
        }

        /// <summary>The best single work (not a collection) by the same author holding typed words the top lacks.</summary>
        public Candidate? Sibling { get; }

        /// <summary>Words for the follow-up OR search; empty when one sibling already holds every leftover title word.</summary>
        public IReadOnlyList<string> ProbeTokens { get; }

        public static Mashup? Find(List<Candidate> ranked, RankingContext context)
        {
            var top = ranked[0];
            if (context.Hypothesis is not { HasTitle: true } || top.Tier is not (MatchTier.A or MatchTier.B) || top.ExactTyped || top.Primary.Count == 0)
                return null;
            var typed = top.NonAuthorTokens.Where(t => t.Len() >= 3 && !TextFolding.IsDigits(t) && !context.HypothesisCharacterTokens.Contains(t)
                                                && !Vocabulary.Noise.Contains(t)).Distinct().ToList();
            var held = typed.Where(t => Holds(top, t)).ToList();
            if (held.Count == 0) return null; // nothing typed supports the top: an unsupported guess, not a mashup
            var leftover = typed.Where(t => !held.Contains(t)).ToList();
            if (leftover.Count == 0) return null;

            var holders = ranked.Skip(1)
                .Where(o => o.TitleKey != top.TitleKey && o.Primary.Overlaps(top.Primary))
                .Select(o => (Work: o, Hits: leftover.Where(t => Holds(o, t)).ToList()))
                .Where(p => p.Hits.Count > 0)
                .ToList();
            if (holders.Count == 0) return null;
            // The strongest holder must hold ≥ 2 leftover words: one shared word ("… on a train") is chatter, not a second title.
            var strongest = holders[0];
            foreach (var p in holders) if (p.Hits.Count > strongest.Hits.Count) strongest = p;
            if (strongest.Hits.Count < 2) return null;
            var words = leftover.Where(t => holders.Any(p => p.Hits.Contains(t))).ToList(); // every word a same-author title holds
            var wanted = held.Concat(words).ToList();
            // One single work by that author holding every typed title word is not a mashup: it is simply the better candidate.
            if (ranked.Any(o => o.Primary.Overlaps(top.Primary) && !WorkMarkers.IsOmnibus(o.FoldedTitle) && wanted.All(t => Holds(o, t)))) return null;

            (Candidate Work, int Hits)? best = null;
            foreach (var p in holders) // ranked order, so ties keep the higher-ranked work
            {
                if (WorkMarkers.IsOmnibus(p.Work.FoldedTitle)) continue;
                var n = p.Hits.Count(words.Contains);
                if (n > 0 && (best is null || n > best.Value.Hits)) best = (p.Work, n);
            }
            var probe = best is { } b && b.Hits == words.Count
                ? []
                : (IReadOnlyList<string>)[PersonNames.Surname(top.Authors.First(a => top.Primary.Contains(a.NameKey)).Display), .. words];
            return new Mashup(best?.Work, probe);
        }

        private static bool Holds(Candidate c, string token)
            => c.Full.Any(w => TokenMatch.Equal(token, w)) || c.EditionSets.Any(e => e.Full.Any(w => TokenMatch.Equal(token, w)));
    }
}
