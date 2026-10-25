using System.Text.RegularExpressions;
using System.Web;
using FindThatBook.Core.Ports;

namespace FindThatBook.Eval.Replay;

/// <summary>
/// The search.json parameters that decide the answer: q, title, author, author_key, sort. Values are URL-decoded with
/// whitespace collapsed; fields and limit are deliberately not part of the key (a recording made with other fields or a
/// larger limit still answers the request; <see cref="RecordedResponseIndex"/> prefers the closest one).
/// </summary>
public sealed partial record RequestKey(string? Q, string? Title, string? Author, string? AuthorKey, string? Sort)
{
    public static RequestKey From(CatalogQuery query) => new(
        Norm(query.Q), Norm(query.Title), Norm(query.Author), Norm(query.AuthorKey),
        query.Sort == CatalogSort.Editions ? "editions" : null);

    /// <summary>
    /// Parses the trace form the pipeline logs (CatalogQuery.ToString: "q=…&amp;title=…&amp;sort=editions&amp;limit=20&amp;person").
    /// Values are not encoded there, so a value containing "&amp;" splits wrongly; that only affects attribution, never replay.
    /// </summary>
    public static RequestKey FromTrace(string request)
    {
        string? q = null, title = null, author = null, authorKey = null, sort = null;
        foreach (var part in request.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var value = part[(eq + 1)..];
            switch (part[..eq])
            {
                case "q": q = value; break;
                case "title": title = value; break;
                case "author": author = value; break;
                case "author_key": authorKey = value; break;
                case "sort": sort = value; break;
            }
        }
        return new RequestKey(Norm(q), Norm(title), Norm(author), Norm(authorKey), sort == "relevance" ? null : Norm(sort));
    }

    /// <summary>Parses a recorded URL. Null when it is not a comparable /search.json request (other path, paging).</summary>
    public static ParsedUrl? Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.AbsolutePath.EndsWith("/search.json", StringComparison.Ordinal))
            return null;
        // ParseQueryString decodes %XX and '+' (Python's urlencode writes spaces as '+').
        var p = HttpUtility.ParseQueryString(uri.Query);
        if (Int(p["offset"]) is > 0 || Int(p["page"]) is > 1) return null;
        var sort = Norm(p["sort"]);
        if (sort == "relevance") sort = null;
        var key = new RequestKey(Norm(p["q"]), Norm(p["title"]), Norm(p["author"]), Norm(p["author_key"]), sort);
        var fields = (p["fields"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return new ParsedUrl(key, Int(p["limit"]), fields);
    }

    /// <summary>Canonical text, e.g. "q=tolkien hobbit" or "title=hobbit&amp;author=dixon&amp;sort=editions".</summary>
    public string Text => string.Join('&', new[]
    {
        Q is null ? null : $"q={Q}", Title is null ? null : $"title={Title}", Author is null ? null : $"author={Author}",
        AuthorKey is null ? null : $"author_key={AuthorKey}", Sort is null ? null : $"sort={Sort}",
    }.Where(x => x is not null));

    /// <summary>Lookup key: field names plus a separator no query contains, so "q=x&amp;title=y" can never equal q=x + title=y.</summary>
    public string IndexKey => string.Join('\u001f', $"q\u001e{Q}", $"title\u001e{Title}", $"author\u001e{Author}", $"author_key\u001e{AuthorKey}", $"sort\u001e{Sort}");

    public override string ToString() => Text;

    private static string? Norm(string? s)
    {
        if (s is null) return null;
        var collapsed = Spaces().Replace(s, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static int? Int(string? s) => int.TryParse(s, out var n) ? n : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <param name="Limit">The recorded limit (null = Open Library's default of 100).</param>
public sealed record ParsedUrl(RequestKey Key, int? Limit, IReadOnlySet<string> Fields);
