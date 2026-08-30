using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Core.Parsing;

namespace OpenLibraryClient.Tests.Parsing;

public class DeterministicParserTests
{
    private readonly DeterministicParser _parser = new();

    [Theory]
    [InlineData("\"Dune\" by Frank Herbert", "Dune", "Frank Herbert")]
    [InlineData("Dune by Frank Herbert", "Dune", "Frank Herbert")]
    [InlineData("The Hobbit by J.R.R. Tolkien", "The Hobbit", "J.R.R. Tolkien")]
    [InlineData("1984 - George Orwell", "1984", "George Orwell")]
    [InlineData("Neuromancer (William Gibson)", "Neuromancer", "William Gibson")]
    public void Parse_RecognizedSeparator_ExtractsTitleAndAuthor(string input, string expectedTitle, string expectedAuthor)
    {
        var result = _parser.Parse(input);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedAuthor, result.Author);
        Assert.Equal(ExtractionSource.Deterministic, result.Source);
        Assert.True(result.Confidence > 0.5);
        Assert.Equal("input clear enough for deterministic match", result.Explanation);
    }

    [Theory]
    [InlineData("\"Title\" by Author", 0.95)]
    [InlineData("Title by Author", 0.85)]
    [InlineData("Title (Author)", 0.80)]
    [InlineData("Title - Author", 0.75)]
    [InlineData("Title, Author", 0.55)]
    public void Parse_DifferentSeparatorStyles_YieldExpectedConfidence(string input, double expectedConfidence)
    {
        var result = _parser.Parse(input);

        Assert.Equal(expectedConfidence, result.Confidence, precision: 2);
    }

    [Fact]
    public void Parse_NoRecognizableSeparator_ReturnsLowConfidenceKeywordBag()
    {
        var result = _parser.Parse("something about space wizards maybe herbert dune");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.True(result.Confidence <= 0.3);
        Assert.NotEmpty(result.Keywords);
        Assert.NotNull(result.Explanation);
    }

    [Fact]
    public void Parse_KeywordsExcludeStopWordsAndTitleAuthorTokens()
    {
        var result = _parser.Parse("Dune by Frank Herbert, sci-fi desert planet");

        Assert.DoesNotContain("dune", result.Keywords);
        Assert.DoesNotContain("frank", result.Keywords);
        Assert.Contains("desert", result.Keywords);
        Assert.Contains("planet", result.Keywords);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsLowConfidenceResultWithoutThrowing()
    {
        var result = _parser.Parse("   ");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.True(result.Confidence <= 0.3);
    }

    [Fact]
    public void Parse_PreservesRawQuery()
    {
        const string input = "  Dune by Frank Herbert  ";

        var result = _parser.Parse(input);

        Assert.Equal(input, result.RawQuery);
    }
}
