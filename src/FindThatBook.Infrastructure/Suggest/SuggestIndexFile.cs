using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FindThatBook.Infrastructure.Suggest;

/// <summary>One work in the index, in rank order (most editions first).</summary>
internal sealed record SuggestRow(string WorkId, string Title, string? Author, int? FirstPublishYear, int? CoverId, int EditionCount);

/// <summary>
/// The committed index file (<c>suggest-index.json.gz</c>): a small header plus one compact array per work, one per
/// line so a regenerated file diffs readably:
/// <code>{"format":1,"source":"…","dump_date":"2026-09-30","built":"2026-10-24","count":50000,"fields":[…],"works":[
/// ["OL66554W","Pride and Prejudice","Jane Austen",1813,14348537,4042],
/// …]}</code>
/// </summary>
internal sealed record SuggestIndexFile(int Format, string Source, string DumpDate, string Built, IReadOnlyList<SuggestRow> Works)
{
    public const int CurrentFormat = 1;
    public const string OpenLibrarySource = "Open Library data dumps, CC0 1.0 (https://openlibrary.org/developers/dumps)";
    static readonly string[] Fields = ["work_id", "title", "author", "first_publish_year", "cover_id", "edition_count"];

    /// <summary>Index version for /api/health: format + source dump date.</summary>
    public string Version => $"{Format}-{DumpDate}";

    public static SuggestIndexFile Read(Stream gzipped)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(gzipped, CompressionMode.Decompress, leaveOpen: true)) gzip.CopyTo(buffer);
        using var doc = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        var root = doc.RootElement;

        var format = root.GetProperty("format").GetInt32();
        if (format != CurrentFormat) throw new InvalidDataException($"Suggest index format {format}; this build reads {CurrentFormat}.");
        var works = new List<SuggestRow>(root.GetProperty("count").GetInt32());
        foreach (var row in root.GetProperty("works").EnumerateArray())
        {
            works.Add(new SuggestRow(
                row[0].GetString()!,
                row[1].GetString()!,
                row[2].ValueKind == JsonValueKind.Null ? null : row[2].GetString(),
                IntOrNull(row[3]),
                IntOrNull(row[4]),
                row[5].GetInt32()));
        }
        if (works.Count != root.GetProperty("count").GetInt32()) throw new InvalidDataException("Suggest index count does not match its rows.");
        return new SuggestIndexFile(format, root.GetProperty("source").GetString()!, root.GetProperty("dump_date").GetString()!,
            root.GetProperty("built").GetString()!, works);
    }

    public void Write(Stream destination)
    {
        using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true);
        using var text = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\n" };
        text.Write($"{{\"format\":{Format},\"source\":{Json(Source)},\"dump_date\":{Json(DumpDate)},\"built\":{Json(Built)},");
        text.Write($"\"count\":{Works.Count},\"fields\":[{string.Join(',', Fields.Select(Json))}],\"works\":[");
        for (var i = 0; i < Works.Count; i++)
        {
            var w = Works[i];
            text.WriteLine(i == 0 ? "" : ",");
            text.Write($"[{Json(w.WorkId)},{Json(w.Title)},{Json(w.Author)},{Json(w.FirstPublishYear)},{Json(w.CoverId)},{w.EditionCount}]");
        }
        text.WriteLine("]}");
    }

    static int? IntOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    // Relaxed escaping keeps 三体 and Анна Каренина readable (and small) in the file; it is never embedded in HTML.
    static string Json(string? s) => s is null ? "null" : $"\"{JsonEncodedText.Encode(s, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"";

    static string Json(int? n) => n?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";
}
