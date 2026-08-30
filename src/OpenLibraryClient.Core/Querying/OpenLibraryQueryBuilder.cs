using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Core.Querying;

/// <summary>
/// Builds the query string sent to Open Library from whatever structured fields were extracted,
/// falling back progressively to keywords and finally the raw query. Shared by the search
/// orchestrator (for retry attempts) and, if needed, callers that just need a single query.
/// </summary>
public static class OpenLibraryQueryBuilder
{
    public static string Build(ExtractionResult extraction)
    {
        if (!string.IsNullOrWhiteSpace(extraction.Title) && !string.IsNullOrWhiteSpace(extraction.Author))
        {
            return $"{extraction.Title} {extraction.Author}";
        }

        if (!string.IsNullOrWhiteSpace(extraction.Title))
        {
            return extraction.Title;
        }

        if (extraction.Keywords.Count > 0)
        {
            return string.Join(' ', extraction.Keywords);
        }

        return extraction.RawQuery;
    }

    /// <summary>Title-only query, used as a relaxation step when a full title+author query yields nothing.</summary>
    public static string? BuildTitleOnly(ExtractionResult extraction) =>
        string.IsNullOrWhiteSpace(extraction.Title) ? null : extraction.Title;

    /// <summary>Keywords-only query, used as a relaxation step further down the fallback chain.</summary>
    public static string? BuildKeywordsOnly(ExtractionResult extraction) =>
        extraction.Keywords.Count > 0 ? string.Join(' ', extraction.Keywords) : null;
}
