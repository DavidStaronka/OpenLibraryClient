namespace OpenLibraryClient.Core.Models;

/// <summary>
/// The individual sub-scores that make up a <see cref="RankedResult"/>'s composite score.
/// Exposed on the response so the frontend (or a developer) can see *why* a result ranked
/// where it did, and so weights can be tuned later without changing the response shape.
/// </summary>
public sealed record ScoreBreakdown
{
    public required double TitleSimilarity { get; init; }
    public required double AuthorSimilarity { get; init; }
    public required double KeywordOverlap { get; init; }
    public required double PopularityNorm { get; init; }
}

/// <summary>An Open Library candidate paired with its computed relevance score.</summary>
public sealed record RankedResult
{
    public required OpenLibraryDoc Doc { get; init; }
    public required double Score { get; init; }
    public required ScoreBreakdown Breakdown { get; init; }

    /// <summary>
    /// Short, human-readable explanation of why this specific candidate ranked where it did,
    /// derived deterministically from <see cref="Breakdown"/> (see IRankingExplainer). Distinct
    /// from ExtractionResult.Explanation, which explains the query parsing/extraction itself
    /// rather than an individual candidate's ranking.
    /// </summary>
    public required string Explanation { get; init; }
}
