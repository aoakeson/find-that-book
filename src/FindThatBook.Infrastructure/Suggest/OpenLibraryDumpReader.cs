using System.Buffers.Text;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FindThatBook.Infrastructure.Suggest;

/// <summary>
/// Streams the Open Library dumps (tab separated: type, key, revision, last_modified, JSON) into suggest candidates,
/// with the same rules as the local mirror (ol-mirror enrich.py + build.py): edition_count = editions whose first
/// <c>works</c> key is the work; first_publish_year = the earliest year in those editions' <c>publish_date</c>;
/// title and first positive cover from the work; primary author = the first listed author that has a name.
/// Three sequential passes (editions, works, authors), bytes only, no per-line strings except for kept rows.
/// </summary>
internal static partial class OpenLibraryDumpReader
{
    internal sealed record DumpFiles(string Works, string Editions, string Authors, string? Date);

    delegate void LineHandler(ReadOnlySpan<byte> line);

    public static DumpFiles Locate(string directory)
    {
        string Find(string kind)
        {
            var named = Directory.GetFiles(directory, $"ol_dump_{kind}_*.txt.gz").Order(StringComparer.Ordinal).LastOrDefault();
            var plain = Path.Combine(directory, $"{kind}.txt.gz");
            return named ?? (File.Exists(plain) ? plain : throw new FileNotFoundException($"No {kind} dump in {directory}.", plain));
        }
        var works = Find("works");
        var date = DumpDate().Match(Path.GetFileName(works));
        return new DumpFiles(works, Find("editions"), Find("authors"), date.Success ? date.Groups[1].Value : null);
    }

    public static List<SuggestRow> ReadCandidates(DumpFiles dumps, int minEditions, Action<string>? log)
    {
        var editions = CountEditions(dumps.Editions, log);
        var works = ReadWorks(dumps.Works, editions, minEditions, log);
        var names = ReadAuthorNames(dumps.Authors, [.. works.Values.SelectMany(w => w.AuthorIds)], log);

        var rows = new List<SuggestRow>(works.Count);
        foreach (var (id, work) in works)
        {
            var author = work.AuthorIds.Select(a => names.GetValueOrDefault(a)).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            var year = editions.MinYear(id);
            rows.Add(new SuggestRow($"OL{id}W", work.Title, author, year == 0 ? null : year, work.Cover, editions.Count(id)));
        }
        log?.Invoke($"candidates: {rows.Count:N0} works with >= {minEditions} editions");
        return rows;
    }

    /// <summary>Edition count and earliest year per work number (OL123W → 123), in flat arrays: 40M+ works fit easily.</summary>
    internal sealed class EditionStats
    {
        int[] counts = new int[1 << 20];
        short[] minYears = new short[1 << 20];

        public int Count(int work) => work < counts.Length ? counts[work] : 0;
        public int MinYear(int work) => work < minYears.Length ? minYears[work] : 0;

        public void Add(int work, int year)
        {
            if (work >= counts.Length)
            {
                var size = Math.Max(work + 1, counts.Length * 3 / 2);
                Array.Resize(ref counts, size);
                Array.Resize(ref minYears, size);
            }
            counts[work]++;
            if (year > 0 && (minYears[work] == 0 || year < minYears[work])) minYears[work] = (short)year;
        }
    }

    internal sealed record WorkInfo(string Title, int[] AuthorIds, int? Cover);

    internal static EditionStats CountEditions(string path, Action<string>? log)
    {
        var stats = new EditionStats();
        long lines = 0, skipped = 0;
        ForEachLine(path, line =>
        {
            if (++lines % 10_000_000 == 0) log?.Invoke($"editions: {lines:N0} lines");
            if (!Columns(line, "/type/edition"u8, out _, out var json)) return;
            try
            {
                var (work, year) = ParseEdition(json);
                if (work > 0) stats.Add(work, year);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { skipped++; }
        });
        log?.Invoke($"editions: {lines:N0} lines read, {skipped:N0} unreadable");
        return stats;
    }

    internal static Dictionary<int, WorkInfo> ReadWorks(string path, EditionStats editions, int minEditions, Action<string>? log)
    {
        var works = new Dictionary<int, WorkInfo>();
        long lines = 0;
        ForEachLine(path, line =>
        {
            if (++lines % 10_000_000 == 0) log?.Invoke($"works: {lines:N0} lines");
            if (!Columns(line, "/type/work"u8, out var key, out var json)) return;
            var id = KeyNumber(key, "/works/OL"u8, (byte)'W');
            if (id <= 0 || editions.Count(id) < minEditions) return;
            try
            {
                if (ParseWork(json) is { } work) works[id] = work;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        });
        log?.Invoke($"works: {lines:N0} lines read, {works.Count:N0} kept");
        return works;
    }

    internal static Dictionary<int, string> ReadAuthorNames(string path, HashSet<int> wanted, Action<string>? log)
    {
        var names = new Dictionary<int, string>();
        ForEachLine(path, line =>
        {
            if (!Columns(line, "/type/author"u8, out var key, out var json)) return;
            var id = KeyNumber(key, "/authors/OL"u8, (byte)'A');
            if (!wanted.Contains(id)) return;
            try
            {
                using var doc = JsonDocument.Parse(json.ToArray());
                var name = Text(doc.RootElement, "name") ?? Text(doc.RootElement, "personal_name");
                if (!string.IsNullOrEmpty(name)) names[id] = name;
            }
            catch (JsonException) { }
        });
        log?.Invoke($"authors: {names.Count:N0} of {wanted.Count:N0} names found");
        return names;
    }

    /// <summary>(first works key number, first year in publish_date) of one edition record.</summary>
    internal static (int Work, int Year) ParseEdition(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return (0, 0);
        int work = 0, year = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("works"u8))
            {
                reader.Read();
                var first = reader; // a copy reads the first element; the original skips the whole array
                if (first.TokenType == JsonTokenType.StartArray && first.Read() && first.TokenType == JsonTokenType.StartObject)
                    work = FirstWorkKey(ref first);
                reader.Skip();
            }
            else if (reader.ValueTextEquals("publish_date"u8))
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number)
                    year = FirstYear(reader.TokenType == JsonTokenType.String ? reader.GetString()! : Encoding.UTF8.GetString(reader.ValueSpan));
                else reader.Skip();
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
        return (work, year);
    }

    static int FirstWorkKey(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isKey = reader.ValueTextEquals("key"u8);
            reader.Read();
            if (isKey && reader.TokenType == JsonTokenType.String) return KeyNumber(reader.ValueSpan, "/works/OL"u8, (byte)'W');
            reader.Skip();
        }
        return 0;
    }

    internal static WorkInfo? ParseWork(ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var root = doc.RootElement;
        var title = Text(root, "title");
        if (string.IsNullOrEmpty(title)) return null;

        var authors = new List<int>();
        if (root.TryGetProperty("authors", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var holder = a.TryGetProperty("author", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : a;
                if (Text(holder, "key") is { } key && KeyNumber(Encoding.UTF8.GetBytes(key), "/authors/OL"u8, (byte)'A') is > 0 and var id)
                    authors.Add(id);
            }
        }

        int? cover = null;
        if (root.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
            foreach (var c in covers.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) && n > 0) { cover = n; break; }

        return new WorkInfo(title, [.. authors], cover);
    }

    /// <summary>The first 4-digit year 1000–2099 standing alone in the text (ol-mirror YEAR regex), else 0.</summary>
    internal static int FirstYear(string text)
    {
        var m = Year().Match(text);
        return m.Success ? int.Parse(m.ValueSpan, provider: System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>"/works/OL123W" → 123; anything else → 0.</summary>
    internal static int KeyNumber(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte suffix)
    {
        if (!key.StartsWith(prefix) || key.Length < prefix.Length + 2 || key[^1] != suffix) return 0;
        return Utf8Parser.TryParse(key[prefix.Length..^1], out int n, out var used) && used == key.Length - prefix.Length - 1 ? n : 0;
    }

    /// <summary>Splits "type\tkey\trevision\tlast_modified\tjson" when the type matches.</summary>
    static bool Columns(ReadOnlySpan<byte> line, ReadOnlySpan<byte> type, out ReadOnlySpan<byte> key, out ReadOnlySpan<byte> json)
    {
        key = json = default;
        if (!line.StartsWith(type) || line.Length <= type.Length || line[type.Length] != (byte)'\t') return false;
        var rest = line[(type.Length + 1)..];
        var tab = rest.IndexOf((byte)'\t');
        if (tab < 0) return false;
        key = rest[..tab];
        for (var skip = 0; skip < 2; skip++)
        {
            rest = rest[(tab + 1)..];
            tab = rest.IndexOf((byte)'\t');
            if (tab < 0) return false;
        }
        json = rest[(tab + 1)..];
        if (json.Length > 0 && json[^1] == (byte)'\r') json = json[..^1];
        return json.Length > 0;
    }

    static void ForEachLine(string path, LineHandler handler)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var buffer = new byte[4 << 20];
        var filled = 0;
        while (true)
        {
            if (filled == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2); // a line longer than the buffer
            var read = gzip.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                if (filled > 0) handler(buffer.AsSpan(0, filled));
                return;
            }
            filled += read;
            var data = buffer.AsSpan(0, filled);
            var start = 0;
            int newline;
            while ((newline = data[start..].IndexOf((byte)'\n')) >= 0)
            {
                handler(data.Slice(start, newline));
                start += newline + 1;
            }
            data[start..].CopyTo(buffer);
            filled -= start;
        }
    }

    static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"\b(1[0-9]{3}|20[0-9]{2})\b")]
    private static partial Regex Year();

    [GeneratedRegex(@"_(\d{4}-\d{2}-\d{2})\.txt\.gz$")]
    private static partial Regex DumpDate();
}
