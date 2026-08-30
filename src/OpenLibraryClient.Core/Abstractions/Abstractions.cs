using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Core.Abstractions;

/// <summary>
/// Pure, deterministic, no-I/O parsing of a messy "bookInfo" string using regex separators
/// and fuzzy string matching. Only recognizes unambiguous separators, so a structured result
/// (title/author both populated) is always trustworthy; anything else falls back to an
/// unstructured keyword bag for the LLM to interpret instead.
/// </summary>
public interface IDeterministicParser
{
    ExtractionResult Parse(string bookInfo);
}

/// <summary>
/// LLM-backed extraction used as a fallback when the deterministic parser couldn't identify a
/// title/author at all. Implementations should request structured JSON output matching
/// ExtractionResult's shape and treat the model's output as best-effort, not ground truth.
/// </summary>
public interface ILlmBookInfoExtractor
{
    Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes <see cref="IDeterministicParser"/> and <see cref="ILlmBookInfoExtractor"/>: try
/// deterministic first, only fall back to the LLM when it couldn't identify a title/author.
/// </summary>
public interface IBookInfoExtractor
{
    Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default);
}

/// <summary>Wraps calls to the Open Library Search API.</summary>
public interface IOpenLibraryClient
{
    Task<IReadOnlyList<OpenLibraryDoc>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps fuzzy string similarity scoring (e.g. FuzzySharp) so it can be swapped or mocked
/// independently of the ranker's weighting logic.
/// </summary>
public interface ISimilarityScorer
{
    /// <summary>Returns a normalized 0.0-1.0 similarity score between two strings.</summary>
    double Score(string a, string b);
}

/// <summary>
/// Pure, no-I/O ranking of Open Library candidates against an extraction result. Blends
/// extraction confidence, title/author similarity, keyword overlap, and popularity signals
/// into a single composite score, weighted differently depending on the extraction source.
/// </summary>
public interface IRelevanceRanker
{
    IReadOnlyList<RankedResult> Rank(ExtractionResult extraction, IReadOnlyList<OpenLibraryDoc> candidates);
}

/// <summary>
/// Pure, no-I/O generator of a short, human-readable explanation for why a single candidate
/// ranked where it did, derived entirely from its already-computed <see cref="ScoreBreakdown"/>.
/// Deliberately rule-based rather than LLM-backed: the signals it explains (title/author
/// similarity, keyword overlap, popularity) are always available regardless of whether the
/// upstream extraction came from the deterministic parser or the LLM, so per-result ranking
/// explanations never need an extra LLM call.
/// </summary>
public interface IRankingExplainer
{
    string Explain(
        ExtractionResult extraction,
        ScoreBreakdown breakdown,
        IReadOnlyList<string> matchedKeywords,
        bool isMostPopularInResults);
}

/// <summary>
/// Orchestrates the full pipeline: extraction, progressively relaxed Open Library query attempts
/// when a query yields zero results or (for a deterministic extraction) an unvalidated
/// top-ranked match (dropping author, consulting the LLM as a second opinion, falling back to
/// keywords, and finally the raw query), and ranking of whichever attempt is ultimately used.
/// </summary>
public interface IBookSearchService
{
    Task<BookSearchResult> SearchAsync(string bookInfo, CancellationToken cancellationToken = default);
}
