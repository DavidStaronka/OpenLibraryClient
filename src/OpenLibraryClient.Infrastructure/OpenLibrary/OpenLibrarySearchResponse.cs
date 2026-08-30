using System.Text.Json.Serialization;

namespace OpenLibraryClient.Infrastructure.OpenLibrary;

/// <summary>
/// Raw JSON shape returned by https://openlibrary.org/search.json. Only fields we explicitly
/// request via the `fields` query parameter will be populated - see OpenLibraryApiClient.
/// </summary>
internal sealed class OpenLibrarySearchResponse
{
    [JsonPropertyName("docs")]
    public List<OpenLibrarySearchDoc> Docs { get; set; } = [];
}

internal sealed class OpenLibrarySearchDoc
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author_name")]
    public List<string>? AuthorName { get; set; }

    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }

    [JsonPropertyName("edition_count")]
    public int EditionCount { get; set; }

    [JsonPropertyName("subject")]
    public List<string>? Subject { get; set; }

    [JsonPropertyName("ratings_average")]
    public double? RatingsAverage { get; set; }

    [JsonPropertyName("want_to_read_count")]
    public int? WantToReadCount { get; set; }

    [JsonPropertyName("cover_i")]
    public int? CoverI { get; set; }
}
