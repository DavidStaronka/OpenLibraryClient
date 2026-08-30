namespace OpenLibraryClient.Core.Models;

/// <summary>
/// The outcome of the full search orchestration pipeline: extraction -> (possibly several
/// relaxed/retried) Open Library queries -> ranking. <see cref="Extraction"/> reflects whichever
/// extraction ultimately produced results (the original deterministic/LLM extraction, or a late
/// LLM "second opinion" triggered after zero results). <see cref="QueriesAttempted"/> records
/// every query string tried, in order, for transparency/debugging.
/// </summary>
public sealed record BookSearchResult
{
    public required ExtractionResult Extraction { get; init; }
    public required IReadOnlyList<RankedResult> Results { get; init; }
    public required IReadOnlyList<string> QueriesAttempted { get; init; }
}
