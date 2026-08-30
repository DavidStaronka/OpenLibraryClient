namespace OpenLibraryClient.Core.Models;

/// <summary>
/// The structured fields we attempt to pull out of a messy, free-text "bookInfo" query, along
/// with a record of which strategy produced the result.
/// </summary>
public sealed record ExtractionResult
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];

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

    /// <summary>
    /// True only for an LLM extraction that failed (after retries) or was skipped because no
    /// Gemini API key is configured - i.e. the zero-value placeholder result
    /// <see cref="OpenLibraryClient.Infrastructure.Extraction.LlmBookInfoExtractor"/> returns
    /// instead of throwing. Used solely to decide cache-worthiness (a degraded result must never
    /// be cached, so a transient LLM outage doesn't get "stuck" as a cached failure). Always
    /// false for deterministic extractions.
    /// </summary>
    public bool IsDegraded { get; init; }

    public bool HasTitleOrAuthor => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Author);
}
