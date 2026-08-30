using FuzzySharp;
using OpenLibraryClient.Core.Abstractions;

namespace OpenLibraryClient.Core.Parsing;

/// <summary>
/// Wraps FuzzySharp's token-sort ratio to produce a normalized 0.0-1.0 similarity score.
/// Isolated behind ISimilarityScorer so ranking/parsing logic can be unit tested independently
/// of the underlying fuzzy-matching algorithm.
/// </summary>
public sealed class SimilarityScorer : ISimilarityScorer
{
    public double Score(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0.0;
        }

        // TokenSortRatio ignores word order, which suits titles/authors that may be reordered
        // or missing a middle name/initial. It is NOT case-insensitive on its own (e.g. "Frank
        // Herbert" vs. "frank herbert" scores ~85, not 100), so we lowercase both inputs first -
        // casing differences between a user's query and Open Library's data are noise, not a
        // meaningful signal of dissimilarity.
        var ratio = Fuzz.TokenSortRatio(a.ToLowerInvariant(), b.ToLowerInvariant());
        return ratio / 100.0;
    }
}
