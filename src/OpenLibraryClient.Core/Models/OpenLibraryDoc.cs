namespace OpenLibraryClient.Core.Models;

/// <summary>
/// A single work document returned from the Open Library Search API
/// (https://openlibrary.org/dev/docs/api/search). Only the fields we explicitly request
/// via the `fields` query parameter are populated - see IOpenLibraryClient for the field list.
/// </summary>
public sealed record OpenLibraryDoc
{
    /// <summary>The work key, e.g. "/works/OL27448W".</summary>
    public required string Key { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> AuthorNames { get; init; } = [];

    public int? FirstPublishYear { get; init; }

    public int EditionCount { get; init; }

    public IReadOnlyList<string> Subjects { get; init; } = [];

    public double? RatingsAverage { get; init; }

    public int? WantToReadCount { get; init; }

    public int? CoverId { get; init; }
}
