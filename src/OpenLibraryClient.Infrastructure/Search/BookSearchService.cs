using System.Diagnostics;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Core.Querying;

namespace OpenLibraryClient.Infrastructure.Search;

/// <summary>
/// Orchestrates extraction -> Open Library query -> ranking, with a progressively relaxed
/// retry chain when a query returns zero candidates (or, for a deterministic extraction, when
/// the top-ranked candidate isn't a validated match):
///
///   1. Full query (title+author, or whatever the extractor produced).
///   2. Title-only (drops a possibly-misspelled author - a common cause of zero hits).
///   3. LLM "second opinion" - only if the original extraction was deterministic (i.e. we
///      haven't already consulted the LLM), and only if that deterministic attempt didn't
///      produce a validated match (see below). We re-extract with the LLM, re-query, and prefer
///      its results whenever it finds anything; if it finds nothing, we keep going with its
///      (usually more accurate) title/author/keywords for the remaining relaxation steps.
///   4. Keywords-only query.
///   5. Raw, unprocessed query string - Open Library's own tokenization may succeed where our
///      constructed queries didn't.
///
/// "Validated match" (deterministic extraction only): a regex-based split can look confident
/// (e.g. "dune - frank h", "dne - frank herbert", "dune - frankie") while still getting the
/// title or author wrong. Rather than trusting the parse in isolation, we rank whatever
/// candidates the query returned and require the top result to be an exact title AND author
/// match against real Open Library data - otherwise the deterministic guess is treated the same
/// as a zero-result attempt and we fall back to the LLM for a second opinion.
///
/// Each distinct query string is only attempted once; the chain stops as soon as an attempt
/// returns at least one candidate.
/// </summary>
public sealed class BookSearchService(
    IBookInfoExtractor extractor,
    ILlmBookInfoExtractor llmExtractor,
    IOpenLibraryClient openLibraryClient,
    IRelevanceRanker ranker,
    BookSearchMetrics metrics) : IBookSearchService
{
    /// <summary>
    /// Similarity score (see ISimilarityScorer, 0.0-1.0) at or above which a title/author is
    /// considered an exact match for validation purposes - not merely "close enough" like the
    /// per-result ranking explanation's thresholds.
    /// </summary>
    private const double ExactMatchThreshold = 1.0;

    public async Task<BookSearchResult> SearchAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await SearchCoreAsync(bookInfo, cancellationToken);
            metrics.RecordSuccess(result, stopwatch.Elapsed);
            return result;
        }
        catch
        {
            metrics.RecordFailure(stopwatch.Elapsed);
            throw;
        }
    }

    private async Task<BookSearchResult> SearchCoreAsync(string bookInfo, CancellationToken cancellationToken)
    {
        var extraction = await extractor.ExtractAsync(bookInfo, cancellationToken);
        var activeExtraction = extraction;
        var queriesAttempted = new List<string>();

        var candidates = await TryAttemptAsync(OpenLibraryQueryBuilder.Build(extraction));

        if (candidates.Count == 0 &&
            !string.IsNullOrWhiteSpace(extraction.Title) &&
            !string.IsNullOrWhiteSpace(extraction.Author))
        {
            candidates = await TryAttemptAsync(OpenLibraryQueryBuilder.BuildTitleOnly(extraction));
        }

        if (extraction.Source == ExtractionSource.Deterministic && !HasValidatedMatch(extraction, candidates))
        {
            metrics.RecordLlmFallback(candidates.Count == 0 ? "zero-results" : "unvalidated-match");

            var llmExtraction = await llmExtractor.ExtractAsync(bookInfo, cancellationToken);
            var llmCandidates = await TryAttemptAsync(OpenLibraryQueryBuilder.Build(llmExtraction));

            if (llmCandidates.Count == 0 &&
                !string.IsNullOrWhiteSpace(llmExtraction.Title) &&
                !string.IsNullOrWhiteSpace(llmExtraction.Author))
            {
                llmCandidates = await TryAttemptAsync(OpenLibraryQueryBuilder.BuildTitleOnly(llmExtraction));
            }

            if (llmCandidates.Count > 0)
            {
                // The LLM found something where the deterministic parse either found nothing or
                // couldn't be validated - trust the LLM's results over the unvalidated ones.
                activeExtraction = llmExtraction;
                candidates = llmCandidates;
            }
            else if (candidates.Count == 0)
            {
                // Neither attempt found anything yet. Keep going with the LLM's (usually more
                // accurate) title/author/keywords for the remaining relaxation steps below.
                activeExtraction = llmExtraction;
            }
            // Otherwise: the deterministic attempt found unvalidated candidates and the LLM
            // found nothing at all - keep the deterministic candidates as the best available
            // guess rather than discarding them for an empty LLM result.
        }

        if (candidates.Count == 0)
        {
            candidates = await TryAttemptAsync(OpenLibraryQueryBuilder.BuildKeywordsOnly(activeExtraction));
        }

        if (candidates.Count == 0)
        {
            candidates = await TryAttemptAsync(activeExtraction.RawQuery);
        }

        var ranked = ranker.Rank(activeExtraction, candidates);

        return new BookSearchResult
        {
            Extraction = activeExtraction,
            Results = ranked,
            QueriesAttempted = queriesAttempted
        };

        async Task<IReadOnlyList<OpenLibraryDoc>> TryAttemptAsync(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return [];
            }

            if (queriesAttempted.Any(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase)))
            {
                return [];
            }

            queriesAttempted.Add(query);
            return await openLibraryClient.SearchAsync(query, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// True when there's at least one candidate and, if the extraction has a structured
    /// title/author to validate against, the top-ranked candidate is an exact match for both.
    /// Used only to decide whether a deterministic extraction is trustworthy enough to skip the
    /// LLM second opinion - always false for zero candidates.
    /// </summary>
    private bool HasValidatedMatch(ExtractionResult extraction, IReadOnlyList<OpenLibraryDoc> candidates)
    {
        if (candidates.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(extraction.Title) || string.IsNullOrWhiteSpace(extraction.Author))
        {
            // Nothing structured to validate against (shouldn't happen for a Deterministic
            // extraction that passed BookInfoExtractor's confidence gate, but be permissive).
            return true;
        }

        var top = ranker.Rank(extraction, candidates)[0];
        return top.Breakdown.TitleSimilarity >= ExactMatchThreshold && top.Breakdown.AuthorSimilarity >= ExactMatchThreshold;
    }
}
