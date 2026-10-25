using System.Globalization;
using System.Text;

namespace FindThatBook.Core.Normalization;

/// <summary>
/// Input rules shared by the API and the eval (DE-05, IN-001..IN-015). Order: control characters out,
/// trim, length, "has a letter or digit", "has a non-stop-word token". Short and non-Latin queries are valid.
/// </summary>
public static class QueryValidator
{
    /// <summary>Maximum length in UTF-16 code units (C# string.Length; the frontend's maxlength counts the same unit, IN-013).</summary>
    public const int MaxLength = 300;

    public const string ValidationFailed = "validation_failed";
    public const string NoSearchableTerms = "no_searchable_terms";
    public const string NoSearchableTermsHint = "Add a title or author.";

    public static QueryValidation Validate(string? q)
    {
        var query = Sanitize(q);
        if (query.Length == 0)
            return QueryValidation.Invalid(ValidationFailed, "Enter a title, an author or a description of the book.");
        if (query.Length > MaxLength)
            return QueryValidation.Invalid(ValidationFailed, $"The query must be at most {MaxLength} characters.");
        if (!query.EnumerateRunes().Any(Rune.IsLetterOrDigit))
            return QueryValidation.Invalid(ValidationFailed, "The query must contain at least one letter or digit.");
        // DE-05 / IN-006..007: "the", "a of the" (or a pasted link with no readable words) leave nothing to search.
        if (TextFolding.Tokens(QueryMarkup.Clean(query)).All(Vocabulary.Stop.Contains))
            return QueryValidation.Invalid(NoSearchableTerms, NoSearchableTermsHint);
        return QueryValidation.Valid(query);
    }

    /// <summary>
    /// Whitespace control characters (tab, newline) become spaces, other control characters (Cc) and format
    /// characters (Cf: zero-width, bidi marks, soft hyphen, BOM, and the invisible TAG characters U+E0000–U+E007F that
    /// can spell hidden instructions) are removed, then the text is trimmed (IN-002, IN-004, IN-015, IN-019). Checked
    /// per code point, so characters outside the BMP are judged whole, never as surrogate halves. Deleting Cf rather
    /// than spacing it keeps "hob&lt;ZWSP&gt;bit" one word.
    /// </summary>
    public static string Sanitize(string? q)
    {
        if (string.IsNullOrEmpty(q)) return "";
        var sb = new StringBuilder(q.Length);
        foreach (var r in q.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (cat == UnicodeCategory.Control)
            {
                if (Rune.IsWhiteSpace(r)) sb.Append(' ');
            }
            else if (cat != UnicodeCategory.Format) sb.Append(r.ToString());
        }
        return sb.ToString().Trim();
    }
}

/// <param name="Query">The normalized query to search with (trimmed, control/format characters removed) when valid.</param>
/// <param name="ErrorCode">"validation_failed" or "no_searchable_terms" when invalid.</param>
public sealed record QueryValidation(bool IsValid, string? Query, string? ErrorCode, string? Message)
{
    public static QueryValidation Valid(string query) => new(true, query, null, null);
    public static QueryValidation Invalid(string code, string message) => new(false, null, code, message);
}
