using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Eval.Reports;

/// <summary>Everything that identifies a run; stamped on summary.md and summary.json.</summary>
public sealed record RunInfo
{
    public required string Command { get; init; }
    public required string Label { get; init; }
    public required DateTimeOffset Started { get; init; }
    public required TimeSpan Wall { get; init; }
    public required Dataset Dataset { get; init; }
    public required EvalHostOptions Host { get; init; }
    public required CodeFingerprint.Print Source { get; init; }
    public required CodeFingerprint.Print Tool { get; init; }
    public required int Concurrency { get; init; }
    public required int ResultLimit { get; init; }
    public int LlmPace { get; init; }

    /// <summary>--resume: the earlier run whose rows were kept, and how many rows this run re-ran.</summary>
    public string? ResumedFrom { get; init; }
    public int ResumedRows { get; init; }
    public string? StoppedReason { get; init; }

    /// <summary>Replay folders and how many recordings each contributed.</summary>
    public IReadOnlyList<(string Folder, int Files)> ReplaySources { get; init; } = [];

    /// <summary>Hypothesis replay file and its entry count (null = interpretations came from the configured provider).</summary>
    public string? Hypotheses { get; init; }
    public int HypothesisEntries { get; init; }

    /// <summary>Record-on-miss cache: requests fetched from the backend in this run, and calls answered empty as unsupported.</summary>
    public int CacheFills { get; init; }
    public int Unsupported { get; init; }

    public string RankerArm => RankerArms.Describe(Host.Ranker, Host.RankerOff);

    public string CatalogDescription => Host.Catalog switch
    {
        CatalogMode.Live => "live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured)",
        CatalogMode.Mirror => $"MIRROR {Host.MirrorUrl}: synthetic, local mirror (bm25, OR fallback), not Open Library's ranking"
                              + (Host.CatalogCache is null ? "" : "; behind the record-on-miss cache"),
        _ => "replay of recorded Open Library responses (offline; no HTTP, so Open Library latency/attempts are n/a)",
    } + (Host.CatalogCache is { } c && Host.Catalog != CatalogMode.Mirror ? $"; behind the record-on-miss cache `{RepoPaths.Relative(c)}`" : "");

    /// <summary>Times are stamped in US Mountain and Eastern time, with the UTC offset.</summary>
    public static string Stamp(DateTimeOffset t)
    {
        static string In(DateTimeOffset t, string zone, string label)
        {
            try
            {
                var local = TimeZoneInfo.ConvertTime(t, TimeZoneInfo.FindSystemTimeZoneById(zone));
                return $"{local:yyyy-MM-dd HH:mm} {label} ({local:zzz})";
            }
            catch (TimeZoneNotFoundException)
            {
                return $"{t.ToUniversalTime():yyyy-MM-dd HH:mm} UTC";
            }
        }
        return $"{In(t, "America/Denver", "MT")} · {In(t, "America/New_York", "ET")}";
    }
}
