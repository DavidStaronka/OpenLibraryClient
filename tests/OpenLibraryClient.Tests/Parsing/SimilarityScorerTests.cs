using OpenLibraryClient.Core.Parsing;

namespace OpenLibraryClient.Tests.Parsing;

public class SimilarityScorerTests
{
    private readonly SimilarityScorer _scorer = new();

    [Fact]
    public void Score_SameStringDifferentCase_ReturnsExactMatch()
    {
        // FuzzySharp's TokenSortRatio is case-sensitive on its own (~0.85 for this pair); the
        // scorer must normalize case so a mere casing difference isn't treated as dissimilarity.
        var score = _scorer.Score("frank herbert", "Frank Herbert");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Score_IdenticalStrings_ReturnsExactMatch()
    {
        var score = _scorer.Score("Dune", "Dune");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Score_CompletelyDifferentStrings_ReturnsLowScore()
    {
        var score = _scorer.Score("Dune", "Moby Dick");

        Assert.True(score < 0.5);
    }

    [Fact]
    public void Score_EitherStringNullOrWhitespace_ReturnsZero()
    {
        Assert.Equal(0.0, _scorer.Score("", "Dune"));
        Assert.Equal(0.0, _scorer.Score("Dune", "   "));
    }
}
