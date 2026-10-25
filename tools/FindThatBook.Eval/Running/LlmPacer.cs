using System.Diagnostics;

namespace FindThatBook.Eval.Running;

/// <summary>
/// Keeps the eval's LLM calls under a calls-per-minute rate, so the API's own budget guard (AI:&lt;Provider&gt;:RequestsPerMinute, Gemini 15,
/// which refuses calls past the rate as budget_exhausted) and the provider's free-tier limit never turn an LLM-on
/// measurement into a heuristic one. Callers wait instead; the wait is not part of any reported timing. Slots are claimed
/// when a query starts, slightly before its calls are sent, which is why callers pace to 90% of the budget.
/// </summary>
public sealed class LlmPacer(int callsPerMinute)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Queue<TimeSpan> _sent = new();
    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public int CallsPerMinute { get; } = callsPerMinute;

    /// <summary>Waits until <paramref name="calls"/> more calls fit in the last minute, then claims them. 0 calls or rate 0 = no wait.</summary>
    public async Task WaitAsync(int calls, CancellationToken ct)
    {
        if (calls <= 0 || CallsPerMinute <= 0) return;
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = _clock.Elapsed;
                while (_sent.Count > 0 && now - _sent.Peek() >= Window) _sent.Dequeue();
                // A claim bigger than the whole rate (rate 1, a query needing 2) goes alone, once the window is empty.
                if (_sent.Count + calls <= CallsPerMinute || _sent.Count == 0)
                {
                    for (var i = 0; i < calls; i++) _sent.Enqueue(now);
                    return;
                }
                wait = _sent.Peek() + Window - now + TimeSpan.FromMilliseconds(50);
            }
            await Task.Delay(wait, ct);
        }
    }
}
