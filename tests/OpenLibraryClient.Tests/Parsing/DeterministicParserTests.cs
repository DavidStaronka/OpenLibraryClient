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
        Assert.True(result.HasTitleOrAuthor);
        Assert.Equal("input clear enough for deterministic match", result.Explanation);
    }

    [Fact]
    public void Parse_NoRecognizableSeparator_ReturnsUnstructuredKeywordBag()
    {
        var result = _parser.Parse("something about space wizards maybe herbert dune");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.False(result.HasTitleOrAuthor);
        Assert.NotEmpty(result.Keywords);
        Assert.NotNull(result.Explanation);
    }

    [Fact]
    public void Parse_BareCommaSeparator_IsAmbiguousAndTreatedAsKeywordBag()
    {
        // Comma order between title/author is ambiguous, so it's deliberately not recognized as
        // a structured separator; it should fall through to the unstructured keyword bag.
        var result = _parser.Parse("Title, Author");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.False(result.HasTitleOrAuthor);
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
    public void Parse_ShortTitleIsSubstringOfUnrelatedWord_DoesNotCorruptKeywords()
    {
        // "It" (title) is a substring of "Italian" in the remainder; a naive substring replace
        // would strip "It" out of "Italian" too, leaving a mangled "alian" keyword. Word-boundary
        // removal should leave "italian" intact instead.
        var result = _parser.Parse("It by Stephen King, Italian edition");

        Assert.Equal("It", result.Title);
        Assert.Equal("Stephen King", result.Author);
        Assert.Contains("italian", result.Keywords);
        Assert.DoesNotContain("alian", result.Keywords);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsUnstructuredResultWithoutThrowing()
    {
        var result = _parser.Parse("   ");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.False(result.HasTitleOrAuthor);
    }

    [Fact]
    public void Parse_PreservesRawQuery()
    {
        const string input = "  Dune by Frank Herbert  ";

        var result = _parser.Parse(input);

        Assert.Equal(input, result.RawQuery);
    }
}
