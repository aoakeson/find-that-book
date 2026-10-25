using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FindThatBook.Infrastructure.Suggest;

/// <summary>What one index build did: the README and the build log quote these numbers.</summary>
public sealed record SuggestBuildReport(
    string OutputPath, int Count, int Candidates, int Invalid, int LowContent, int Duplicates, int MinEditionCount, long Bytes);

/// <summary>
/// Build-time only, never on the request path: turns the Open Library dumps (CC0) into the committed
/// <c>suggest-index.json.gz</c>. Steps: tidy the fields, drop low-content print-on-demand, rank by
/// edition count, dedupe by title key + primary author keeping the most editions, keep the top 50,000.
/// Deterministic: the same dumps and build date give the same rows. Run it with the opt-in
/// <c>SuggestIndexRegenerationTests</c> (README: "Regenerating the suggest index").
/// </summary>
public static partial class SuggestIndexBuilder
{
    public const int DefaultCount = 50_000;

    /// <summary>Works with fewer editions never reach the top 50,000 (the cut is ≈ 15), so they are not read.</summary>
    public const int MinCandidateEditions = 10;

    /// <summary>
    /// Build from a folder holding the works, editions and authors dumps (<c>ol_dump_works_YYYY-MM-DD.txt.gz</c> or
    /// <c>works.txt.gz</c>, …). The dump date comes from the file names unless given. Reads ≈ 17 GB gzipped: minutes.
    /// </summary>
    public static SuggestBuildReport BuildFromDumps(string dumpDirectory, string outputPath, DateOnly builtOn,
        string? dumpDate = null, int count = DefaultCount, Action<string>? log = null)
    {
        var dumps = OpenLibraryDumpReader.Locate(dumpDirectory);
        dumpDate ??= dumps.Date ?? throw new ArgumentException("The dump file names carry no date; pass dumpDate (yyyy-MM-dd).", nameof(dumpDate));
        var candidates = OpenLibraryDumpReader.ReadCandidates(dumps, MinCandidateEditions, log);
        return Save(Build(candidates, dumpDate, builtOn, count), outputPath);
    }

    /// <summary>
    /// Build from a candidates file already extracted from the same dumps: a gzipped JSON array of
    /// <c>{"w":work id,"t":title,"a":primary author,"y":year,"c":cover id,"e":edition count}</c>.
    /// Seconds instead of minutes; same rules.
    /// </summary>
    public static SuggestBuildReport BuildFromCandidates(string candidatesPath, string outputPath, string dumpDate, DateOnly builtOn,
        int count = DefaultCount)
    {
        using var file = File.OpenRead(candidatesPath);
        return Save(Build(ReadCandidatesFile(file), dumpDate, builtOn, count), outputPath);
    }

    internal sealed record BuildResult(SuggestIndexFile File, int Candidates, int Invalid, int LowContent, int Duplicates);

    internal static BuildResult Build(IEnumerable<SuggestRow> candidates, string dumpDate, DateOnly builtOn, int count = DefaultCount)
    {
        if (!DateOnly.TryParseExact(dumpDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException($"Dump date '{dumpDate}' is not yyyy-MM-dd.", nameof(dumpDate));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        int total = 0, invalid = 0, lowContent = 0, duplicates = 0;
        var clean = new List<SuggestRow>();
        foreach (var candidate in candidates)
        {
            total++;
            var row = Clean(candidate, maxYear: builtOn.Year + 1);
            if (row is null) invalid++;
            else if (IsLowContent(row)) lowContent++;
            else clean.Add(row);
        }
        clean.Sort(static (a, b) => b.EditionCount != a.EditionCount
            ? b.EditionCount.CompareTo(a.EditionCount)
            : string.CompareOrdinal(a.WorkId, b.WorkId));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<SuggestRow>(Math.Min(count, clean.Count));
        foreach (var row in clean)
        {
            if (kept.Count == count) break;
            if (!ids.Add(row.WorkId) || !keys.Add(SuggestText.DedupeKey(row.Title, row.Author))) { duplicates++; continue; }
            kept.Add(row);
        }

        var file = new SuggestIndexFile(SuggestIndexFile.CurrentFormat, SuggestIndexFile.OpenLibrarySource, dumpDate,
            builtOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), kept);
        return new BuildResult(file, total, invalid, lowContent, duplicates);
    }

    /// <summary>
    /// Trimmed, single-spaced fields; implausible years (≤ 0 or in the future, R5) and covers become null. A work with
    /// no named author is dropped: in the top 50k those are serials and catalog records ("Macmillan's magazine",
    /// "CDROM"), and every suggestion shows "title · author".
    /// </summary>
    internal static SuggestRow? Clean(SuggestRow row, int maxYear)
    {
        var title = Squash(row.Title);
        var author = Squash(row.Author ?? "");
        if (title.Length == 0 || author.Length == 0 || row.EditionCount <= 0 || string.IsNullOrWhiteSpace(row.WorkId)) return null;
        return row with
        {
            Title = title,
            Author = author,
            FirstPublishYear = row.FirstPublishYear is { } y && y > 0 && y <= maxYear ? y : null,
            CoverId = row.CoverId is > 0 ? row.CoverId : null,
        };
    }

    /// <summary>
    /// Blank notebooks, planners, log books, colouring and puzzle books first published 2010 or later: print-on-demand
    /// listings with hundreds of ISBN "editions" that nobody looks up by title ("Mileage Log Book" outranked Macbeth).
    /// Older titles with these words are real books (The Diary of a Nobody, Diary of a Wimpy Kid 2007, The Notebook).
    /// </summary>
    internal static bool IsLowContent(SuggestRow row)
    {
        if (row.FirstPublishYear is not { } year || year < 2010) return false;
        if (LowContentTitle().IsMatch(row.Title)) return true;
        var words = SuggestText.TitleForms(row.Title).FirstOrDefault();
        var suspectAuthor = row.Author is { } author &&
            (LowContentTitle().IsMatch(author) || RepeatedAuthorWords().IsMatch(author) ||
             (words is { Length: 1 } && AuthorEchoesSingleWordTitle(author, words[0])));
        if (!suspectAuthor) return false;
        return words is { Length: 1 } || LowContentTitle().IsMatch(row.Author!);
    }

    private static bool AuthorEchoesSingleWordTitle(string author, string title) =>
        !author.Contains(' ') && author.Length > title.Length &&
        author.EndsWith(title, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b(.+?\b)\s+\1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedAuthorWords();

    [GeneratedRegex(@"\b(notebooks?|journals?|diary|diaries|planners?|agenda|log ?books?|colou?ring|sudoku|word ?search|crosswords?|mazes|activity ?books?|sketch ?books?|composition|cuaderno|libreta|carnet|cahier|notizbuch|malbuch|maltagebuch|password|lined|ruled|blank|tracker|calendar|organizer)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LowContentTitle();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    static string Squash(string text) => Whitespace().Replace(text, " ").Trim();

    internal static IEnumerable<SuggestRow> ReadCandidatesFile(Stream gzipped)
    {
        using var gzip = new GZipStream(gzipped, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var rows = new List<SuggestRow>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            rows.Add(new SuggestRow(e.GetProperty("w").GetString()!, e.GetProperty("t").GetString() ?? "",
                e.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
                IntOrNull(e, "y"), IntOrNull(e, "c"), e.GetProperty("e").GetInt32()));
        }
        return rows;
    }

    static int? IntOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    static SuggestBuildReport Save(BuildResult result, string outputPath)
    {
        var temp = outputPath + ".tmp";
        using (var stream = File.Create(temp)) result.File.Write(stream);
        File.Move(temp, outputPath, overwrite: true);
        var works = result.File.Works;
        return new SuggestBuildReport(Path.GetFullPath(outputPath), works.Count, result.Candidates, result.Invalid, result.LowContent,
            result.Duplicates, works.Count == 0 ? 0 : works[^1].EditionCount, new FileInfo(outputPath).Length);
    }
}
