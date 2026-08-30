using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Core.Ranking;

/// <summary>
/// Ranks Open Library candidates against an ExtractionResult using a composite score that
/// blends title/author similarity, keyword overlap, and popularity, weighted differently
/// depending on how the extraction was produced:
///
/// - Deterministic (an unambiguous separator was matched) -> weight title/author similarity
///   heavily, since we trust the parsed title/author outright.
/// - Llm (fallback, used precisely because the input was ambiguous) -> weight keyword overlap
///   and popularity more, since the parsed title/author itself is a guess and needs
///   corroborating signal.
/// </summary>
public sealed class RelevanceRanker(ISimilarityScorer similarityScorer, IRankingExplainer explainer) : IRelevanceRanker
{
    private readonly record struct Weights(double Title, double Author, double Keyword, double Popularity);

    private static readonly Weights DeterministicWeights = new(Title: 0.40, Author: 0.35, Keyword: 0.15, Popularity: 0.10);
    private static readonly Weights LlmWeights = new(Title: 0.25, Author: 0.20, Keyword: 0.35, Popularity: 0.20);

    public IReadOnlyList<RankedResult> Rank(ExtractionResult extraction, IReadOnlyList<OpenLibraryDoc> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var weights = extraction.Source == ExtractionSource.Deterministic ? DeterministicWeights : LlmWeights;
        var maxEditionCount = candidates.Max(c => c.EditionCount);

        var ranked = candidates
            .Select(doc => Score(extraction, doc, weights, maxEditionCount))
            .OrderByDescending(r => r.Score)
            .ToList();

        return ranked;
    }

    private RankedResult Score(ExtractionResult extraction, OpenLibraryDoc doc, Weights weights, int maxEditionCount)
    {
        var titleQuery = string.IsNullOrWhiteSpace(extraction.Title) ? extraction.RawQuery : extraction.Title;
        var titleSimilarity = similarityScorer.Score(titleQuery, doc.Title);

        var authorSimilarity = string.IsNullOrWhiteSpace(extraction.Author) || doc.AuthorNames.Count == 0
            ? 0.0
            : doc.AuthorNames.Max(name => similarityScorer.Score(extraction.Author, name));

        var (keywordOverlap, matchedKeywords) = ComputeKeywordOverlap(extraction.Keywords, doc.Subjects);

        // Log-scaled and normalized against the max edition count in this candidate set, so a
        // well-known book scores higher than an obscure one with similar title/author match.
        var popularityNorm = maxEditionCount <= 0
            ? 0.0
            : Math.Log(1 + doc.EditionCount) / Math.Log(1 + Math.Max(maxEditionCount, 1));

        var score =
            weights.Title * titleSimilarity +
            weights.Author * authorSimilarity +
            weights.Keyword * keywordOverlap +
            weights.Popularity * popularityNorm;

        var breakdown = new ScoreBreakdown
        {
            TitleSimilarity = titleSimilarity,
            AuthorSimilarity = authorSimilarity,
            KeywordOverlap = keywordOverlap,
            PopularityNorm = popularityNorm
        };

        var isMostPopularInResults = maxEditionCount > 0 && doc.EditionCount == maxEditionCount;
        var explanation = explainer.Explain(extraction, breakdown, matchedKeywords, isMostPopularInResults);

        return new RankedResult
        {
            Doc = doc,
            Score = score,
            Breakdown = breakdown,
            Explanation = explanation
        };
    }

    private static (double Overlap, IReadOnlyList<string> Matched) ComputeKeywordOverlap(IReadOnlyList<string> keywords, IReadOnlyList<string> subjects)
    {
        if (keywords.Count == 0 || subjects.Count == 0)
        {
            return (0.0, []);
        }

        var matched = keywords
            .Where(keyword => subjects.Any(subject => subject.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return ((double)matched.Count / keywords.Count, matched);
    }
}
