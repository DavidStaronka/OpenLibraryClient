using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Core.Ranking;

namespace OpenLibraryClient.Tests.Ranking;

public class RankingExplainerTests
{
    private readonly RankingExplainer _explainer = new();

    private static ExtractionResult Extraction(string? title, string? author) => new()
    {
        Title = title,
        Author = author,
        Keywords = [],
        Confidence = 0.85,
        Source = ExtractionSource.Deterministic,
        RawQuery = "raw query"
    };

    private static ScoreBreakdown Breakdown(
        double title = 0.0,
        double author = 0.0,
        double keyword = 0.0,
        double popularity = 0.0,
        double confidence = 0.85) => new()
    {
        TitleSimilarity = title,
        AuthorSimilarity = author,
        KeywordOverlap = keyword,
        PopularityNorm = popularity,
        ExtractionConfidence = confidence
    };

    [Fact]
    public void Explain_StrongTitleAndAuthorMatch_MentionsBothAsVeryCloseMatches()
    {
        var explanation = _explainer.Explain(
            Extraction("Dune", "Frank Herbert"),
            Breakdown(title: 0.95, author: 0.92),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Contains("title is a very close match", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("author is a very close match", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_ExactTitleAndAuthorMatch_MentionsBothAsExactMatches()
    {
        var explanation = _explainer.Explain(
            Extraction("Dune", "Frank Herbert"),
            Breakdown(title: 1.0, author: 1.0),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Contains("title is an exact match", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("author is an exact match", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very close", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_ModerateTitleMatch_MentionsSimilarNotVeryClose()
    {
        var explanation = _explainer.Explain(
            Extraction("Dune", author: null),
            Breakdown(title: 0.7),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Contains("title is similar", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very close", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_WeakSimilarity_OmitsFieldEntirely()
    {
        var explanation = _explainer.Explain(
            Extraction("Dune", "Frank Herbert"),
            Breakdown(title: 0.2, author: 0.1),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Equal("Matched primarily on the overall search terms.", explanation);
    }

    [Fact]
    public void Explain_MatchedKeywords_NamesUpToThreeOfThem()
    {
        var explanation = _explainer.Explain(
            Extraction(title: null, author: null),
            Breakdown(keyword: 0.9),
            matchedKeywords: ["desert", "planet", "sci-fi", "adventure"],
            isMostPopularInResults: false);

        Assert.Contains("desert", explanation);
        Assert.Contains("planet", explanation);
        Assert.Contains("sci-fi", explanation);
        Assert.DoesNotContain("adventure", explanation);
    }

    [Fact]
    public void Explain_MostPopularInResults_MentionsWidelyPublished()
    {
        var explanation = _explainer.Explain(
            Extraction(title: null, author: null),
            Breakdown(popularity: 1.0),
            matchedKeywords: [],
            isMostPopularInResults: true);

        Assert.Contains("most widely published edition", explanation);
    }

    [Fact]
    public void Explain_StronglyPopularButNotTop_MentionsWellEstablished()
    {
        var explanation = _explainer.Explain(
            Extraction(title: null, author: null),
            Breakdown(popularity: 0.8),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Contains("well-established edition", explanation);
    }

    [Fact]
    public void Explain_NoTitleOrAuthorExtracted_DoesNotMentionThemEvenIfBreakdownHasNonZeroSimilarity()
    {
        // Title/author similarity is computed against the raw query as a fallback even when no
        // structured title/author was extracted; the explanation should only credit fields the
        // extraction actually identified.
        var explanation = _explainer.Explain(
            Extraction(title: null, author: null),
            Breakdown(title: 0.95, author: 0.95),
            matchedKeywords: [],
            isMostPopularInResults: false);

        Assert.Equal("Matched primarily on the overall search terms.", explanation);
    }
}
