using System.Text;

namespace FindThatBook.Infrastructure.Configuration;

/// <summary>
/// A small, dependency-free reader for the repo-root <c>.env</c> file: <c>KEY=VALUE</c> lines, blank lines and
/// <c>#</c> comments, an optional <c>export</c> prefix, single quotes (literal), double quotes (with \n \t \" \\
/// escapes; may span lines) and trailing <c># comments</c> after unquoted values. Lines that don't parse are skipped.
/// Values are never logged or echoed by this code.
/// </summary>
public static class DotEnvFile
{
    /// <summary>Entries in file order. A key that appears twice keeps both entries; the provider lets the last one win.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var entries = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (!IsValidKey(key)) continue;

            var rest = line[(eq + 1)..].TrimStart();
            string? value;
            if (rest.StartsWith('"') || rest.StartsWith('\''))
            {
                value = ReadQuoted(lines, ref i, rest);
                if (value is null) continue; // unterminated quote: skip rather than guess
            }
            else
            {
                value = StripInlineComment(rest).TrimEnd();
            }
            entries.Add(new(key, value));
        }
        return entries;
    }

    internal static bool IsValidKey(string key)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_')) return false;
        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-')) return false;
        }
        return true;
    }

    private static string StripInlineComment(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '#' && (i == 0 || char.IsWhiteSpace(value[i - 1]))) return value[..i];
        }
        return value;
    }

    /// <summary>Reads a quoted value that starts at <paramref name="first"/>, continuing onto later lines if needed.</summary>
    private static string? ReadQuoted(string[] lines, ref int lineIndex, string first)
    {
        var quote = first[0];
        var sb = new StringBuilder();
        var current = first[1..];
        var index = lineIndex;
        while (true)
        {
            for (var i = 0; i < current.Length; i++)
            {
                var c = current[i];
                if (quote == '"' && c == '\\' && i + 1 < current.Length)
                {
                    i++;
                    sb.Append(current[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var other => other });
                    continue;
                }
                if (c == quote)
                {
                    lineIndex = index;
                    return sb.ToString(); // anything after the closing quote is a comment
                }
                sb.Append(c);
            }
            if (++index >= lines.Length) return null;
            sb.Append('\n');
            current = lines[index];
        }
    }
}
