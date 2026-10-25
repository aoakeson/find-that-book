using FindThatBook.Core.Model;

namespace FindThatBook.Core.Matching;

public static partial class MatchRanker
{
    /// <summary>
    /// The ordering score (formula on <see cref="MatchRanker"/>). The terms are added in this fixed order, so the same
    /// inputs always give bit-identical scores.
    /// </summary>
    private static double Score(Candidate work, double titleScore, AuthorRole? role, bool namesOtherAuthor)
    {
        var roleWeight = role switch { AuthorRole.Primary => 1.0, AuthorRole.Contributor => 0.5, _ => 0.0 };
        return Weights.Title * titleScore
               + Weights.AuthorRole * roleWeight
               + Weights.Editions * Math.Log10(1.0 + work.Editions)
               + Weights.Year * (work.YearMatched ? 1.0 : 0.0)
               - MarkerPenalty(work.Penalties)
               + Weights.Characters * work.CharacterCoverage
               + Weights.ReadingLog * Math.Log10(1.0 + work.ReadingLog)
               - Weights.OtherAuthorPenalty * (namesOtherAuthor && role is null ? 1.0 : 0.0);
    }

    /// <summary>Sum of the marker penalties, in marker order.</summary>
    private static double MarkerPenalty(IReadOnlyList<string> markers)
    {
        double sum = 0;
        foreach (var marker in markers)
            sum += marker switch
            {
                WorkMarkers.Omnibus => Weights.OmnibusPenalty,
                WorkMarkers.Study => Weights.StudyGuidePenalty,
                WorkMarkers.Adapt => Weights.AdaptationPenalty,
                _ => Weights.VolumePartPenalty,
            };
        return sum;
    }
}
