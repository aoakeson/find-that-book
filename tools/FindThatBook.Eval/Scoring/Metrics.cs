using System.Globalization;
using FindThatBook.Eval.Datasets;

namespace FindThatBook.Eval.Scoring;

/// <summary>hits / total; prints "52/57 = .912", or "n/a" when nothing was measured.</summary>
public sealed record Ratio(int Hits, int Total)
{
    public double? Value => Total == 0 ? null : (double)Hits / Total;

    public override string ToString() => Total == 0 ? "n/a" : $"{Hits}/{Total} = {Fmt.Rate(Value)}";
}

/// <summary>Quality numbers over a group of rows (the metrics table in eval/README.md). Skipped rows are not part of any group.</summary>
/// <param name="Coverage">Answerable rows returned as match (the auto-accept rate).</param>
/// <param name="FalseMatches">"match" on a row with no answer (none/rejected rows): always a false positive.</param>
/// <param name="SevereMatches">Wrong or false matches, not counting a duplicate record of the right book (battle "severe").</param>
/// <param name="DupRecordMatches">"match" on another record of the intended book (battle dup_record_match, major).</param>
public sealed record QualityStats(
    int Rows,
    int Answerable,
    Ratio Top1,
    Ratio Top5,
    Ratio Status,
    Ratio MatchPrecision,
    Ratio Coverage,
    int NoAnswerRows,
    int FalseMatches,
    int Errors,
    int SevereMatches = 0,
    int DupRecordMatches = 0)
{
    public static QualityStats Compute(IEnumerable<(string Outcome, CaseScore Score)> source)
    {
        var rows = source.Where(r => r.Outcome != Outcomes.Skipped).ToList();
        var answerable = rows.Where(r => r.Score.Answerable).ToList();
        var statusRows = rows.Where(r => r.Score.Status is not null).ToList();
        var matches = rows.Where(r => r.Score.IsMatch).ToList();
        return new QualityStats(
            rows.Count,
            answerable.Count,
            new Ratio(answerable.Count(r => r.Score.Top1 == true), answerable.Count),
            new Ratio(answerable.Count(r => r.Score.Top5 == true), answerable.Count),
            new Ratio(statusRows.Count(r => r.Score.Status == true), statusRows.Count),
            new Ratio(matches.Count(r => r.Score.MatchCorrect == true), matches.Count),
            new Ratio(answerable.Count(r => r.Score.IsMatch), answerable.Count),
            rows.Count(r => !r.Score.Answerable),
            rows.Count(r => r.Score.FalseMatch),
            rows.Count(r => r.Outcome == Outcomes.Error),
            rows.Count(r => r.Score.SevereMatch),
            rows.Count(r => r.Score.DupRecordMatch));
    }
}

public static class Fmt
{
    /// <summary>Research style: ".912"; "1.000"; "n/a".</summary>
    public static string Rate(double? v) =>
        v is not { } x ? "n/a" : x >= 1 ? "1.000" : x.ToString(".000", CultureInfo.InvariantCulture);

    public static string Num(double? v, string format = "0.##") =>
        v is not { } x ? "n/a" : x.ToString(format, CultureInfo.InvariantCulture);

    public static string Ms(double? v) => v is not { } x ? "n/a" : x.ToString("0", CultureInfo.InvariantCulture) + " ms";

    public static string Usd(decimal? v) => v is not { } x ? "n/a" : "$" + x.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Nearest-rank percentile; null for an empty list.</summary>
    public static double? Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return null;
        var rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    public static double? Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list.Average();
    }

    /// <summary>Markdown-safe cell text.</summary>
    public static string Cell(string? s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");
}
