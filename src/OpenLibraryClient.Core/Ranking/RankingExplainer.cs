using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Core.Ranking;

/// <summary>
/// Rule-based (not LLM-backed) generator of a per-candidate ranking explanation from an
/// already-computed <see cref="ScoreBreakdown"/>. Picks out whichever signals (title/author
/// similarity, keyword overlap, popularity) clear a "worth mentioning" threshold, and composes
/// them into a short sentence, most-significant signal first.
/// </summary>
public sealed class RankingExplainer : IRankingExplainer
{
    private const double StrongSimilarityThreshold = 0.85;
    private const double ModerateSimilarityThreshold = 0.6;
    private const double StrongPopularityThreshold = 0.7;

    private const string FallbackExplanation = "Matched primarily on the overall search terms.";

    public string Explain(
        ExtractionResult extraction,
        ScoreBreakdown breakdown,
        IReadOnlyList<string> matchedKeywords,
        bool isMostPopularInResults)
    {
        var signals = new List<(string Text, double Magnitude)>();

        if (!string.IsNullOrWhiteSpace(extraction.Title))
        {
            var titleText = DescribeSimilarity(breakdown.TitleSimilarity, "title");
            if (titleText is not null)
            {
                signals.Add((titleText, breakdown.TitleSimilarity));
            }
        }

        if (!string.IsNullOrWhiteSpace(extraction.Author))
        {
            var authorText = DescribeSimilarity(breakdown.AuthorSimilarity, "author");
            if (authorText is not null)
            {
                signals.Add((authorText, breakdown.AuthorSimilarity));
            }
        }

        if (matchedKeywords.Count > 0)
        {
            var shown = string.Join(", ", matchedKeywords.Take(3).Select(k => $"'{k}'"));
            signals.Add(($"matches searched keyword(s) {shown}", breakdown.KeywordOverlap));
        }

        if (isMostPopularInResults)
        {
            signals.Add(("the most widely published edition among these results", breakdown.PopularityNorm));
        }
        else if (breakdown.PopularityNorm >= StrongPopularityThreshold)
        {
            signals.Add(("a well-established edition with many editions in print", breakdown.PopularityNorm));
        }

        if (signals.Count == 0)
        {
            return FallbackExplanation;
        }

        // Most significant signals first, capped so the explanation stays short and readable.
        var topSignals = signals
            .OrderByDescending(s => s.Magnitude)
            .Take(3)
            .Select(s => s.Text);

        return Capitalize(string.Join("; ", topSignals)) + ".";
    }

    private static string? DescribeSimilarity(double similarity, string fieldName) => similarity switch
    {
        >= 1.0 => $"{fieldName} is an exact match",
        >= StrongSimilarityThreshold => $"{fieldName} is a very close match",
        >= ModerateSimilarityThreshold => $"{fieldName} is similar",
        _ => null
    };

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
