namespace OpenLibraryClient.Core.Models;

/// <summary>
/// The structured fields we attempt to pull out of a messy, free-text "bookInfo" query,
/// along with a confidence score and a record of which strategy produced the result.
/// </summary>
public sealed record ExtractionResult
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// A 0.0-1.0 confidence score. For deterministic extraction this is typically a fuzzy-match
    /// score against known separators/patterns. For LLM extraction this is the model's
    /// self-reported confidence (treated as advisory, not ground truth).
    /// </summary>
    public required double Confidence { get; init; }

    public required ExtractionSource Source { get; init; }

    /// <summary>
    /// A short, human-readable explanation of why this extraction turned out the way it did.
    /// For deterministic extraction this is a fixed string describing the matched pattern's
    /// clarity. For LLM extraction this is the model's own rationale for the fields it returned.
    /// Null when no explanation was produced (e.g. the LLM call failed and we degraded).
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// The original, unmodified query string. Kept alongside the parsed fields so later stages
    /// (e.g. ranking) can fall back to raw-string comparisons when structured fields are sparse.
    /// </summary>
    public required string RawQuery { get; init; }

    public bool HasTitleOrAuthor => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Author);
}
