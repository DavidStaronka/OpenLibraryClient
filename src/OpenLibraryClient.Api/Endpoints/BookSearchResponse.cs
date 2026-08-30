using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Api.Endpoints;

/// <summary>Response contract for GET /api/books/search.</summary>
public sealed record BookSearchResponse
{
    public required string Query { get; init; }
    public required ExtractionSummary Extraction { get; init; }

    /// <summary>
    /// Short, human-readable explanation of why the extractor (deterministic parser or LLM)
    /// arrived at the title/author/keywords used to drive the search below.
    /// </summary>
    public string? Explanation { get; init; }

    public required IReadOnlyList<RankedResultDto> Results { get; init; }
    public required IReadOnlyList<string> QueriesAttempted { get; init; }
}

public sealed record ExtractionSummary
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public required double Confidence { get; init; }
    public required string Source { get; init; }

    public static ExtractionSummary From(ExtractionResult result) => new()
    {
        Title = result.Title,
        Author = result.Author,
        Keywords = result.Keywords,
        Confidence = result.Confidence,
        Source = result.Source.ToString()
    };
}

public sealed record RankedResultDto
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Authors { get; init; } = [];
    public int? FirstPublishYear { get; init; }
    public int EditionCount { get; init; }
    public int? CoverId { get; init; }
    public required double Score { get; init; }
    public required ScoreBreakdown Breakdown { get; init; }

    /// <summary>Human-readable, deterministically-generated explanation of this candidate's ranking.</summary>
    public required string Explanation { get; init; }

    public static RankedResultDto From(RankedResult ranked) => new()
    {
        Key = ranked.Doc.Key,
        Title = ranked.Doc.Title,
        Authors = ranked.Doc.AuthorNames,
        FirstPublishYear = ranked.Doc.FirstPublishYear,
        EditionCount = ranked.Doc.EditionCount,
        CoverId = ranked.Doc.CoverId,
        Score = ranked.Score,
        Breakdown = ranked.Breakdown,
        Explanation = ranked.Explanation
    };
}
