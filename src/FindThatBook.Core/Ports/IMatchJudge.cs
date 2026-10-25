using FindThatBook.Core.Model;

namespace FindThatBook.Core.Ports;

/// <summary>
/// Optional second opinion (OpenAI Decisions). It can only gate status; it never reorders candidates.
/// Implementations never throw (except caller cancellation): trouble is <see cref="JudgeStatus.Unavailable"/>.
/// </summary>
public interface IMatchJudge
{
    /// <summary>False when the judge is switched off or has no key; the pipeline then reports <see cref="JudgeStatus.Off"/>.</summary>
    bool IsEnabled { get; }

    Task<JudgeVerdict> JudgeAsync(JudgeRequest request, CancellationToken ct);
}

/// <summary>Deduped candidates in final code order: identical requests give identical answers, but a different input order
/// can change the pick, so code owns the order.</summary>
public sealed record JudgeRequest(string Query, IReadOnlyList<JudgeCandidate> Candidates);

public sealed record JudgeCandidate(
    string WorkId,
    string Title,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Contributors,
    int? FirstPublishYear,
    int EditionCount);

/// <param name="Pick">The chosen work id, or null for NONE / no verdict.</param>
/// <param name="Probability">Probability of the pick (not "confidence").</param>
public sealed record JudgeVerdict(
    JudgeStatus Status,
    string? Pick = null,
    double? Probability = null,
    string? Model = null,
    long? InputTokens = null,
    long ElapsedMs = 0,
    string? Detail = null)
{
    public static JudgeVerdict Off { get; } = new(JudgeStatus.Off);
    public static JudgeVerdict Skipped(string reason) => new(JudgeStatus.Skipped, Detail: reason);
}
