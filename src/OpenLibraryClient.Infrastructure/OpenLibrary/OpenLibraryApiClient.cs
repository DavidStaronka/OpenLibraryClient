using System.Net.Http.Json;
using System.Web;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.OpenLibrary;

/// <summary>
/// HTTP client for the Open Library Search API (https://openlibrary.org/dev/docs/api/search).
/// Explicitly requests the `fields` we need, since fields like `subject`, `ratings_average`,
/// and `want_to_read_count` are NOT returned by default.
/// </summary>
public sealed class OpenLibraryApiClient(HttpClient httpClient) : IOpenLibraryClient
{
    private const string Fields =
        "key,title,author_name,first_publish_year,edition_count,subject,ratings_average,want_to_read_count,cover_i";

    public async Task<IReadOnlyList<OpenLibraryDoc>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var encodedQuery = HttpUtility.UrlEncode(query);
        var requestUri = $"search.json?q={encodedQuery}&limit={limit}&fields={Fields}";

        var response = await httpClient.GetFromJsonAsync<OpenLibrarySearchResponse>(requestUri, cancellationToken);
        if (response is null)
        {
            return [];
        }

        return response.Docs
            .Where(d => !string.IsNullOrWhiteSpace(d.Key) && !string.IsNullOrWhiteSpace(d.Title))
            .Select(d => new OpenLibraryDoc
            {
                Key = d.Key!,
                Title = d.Title!,
                AuthorNames = d.AuthorName ?? [],
                FirstPublishYear = d.FirstPublishYear,
                EditionCount = d.EditionCount,
                Subjects = d.Subject ?? [],
                RatingsAverage = d.RatingsAverage,
                WantToReadCount = d.WantToReadCount,
                CoverId = d.CoverI
            })
            .ToList();
    }
}
