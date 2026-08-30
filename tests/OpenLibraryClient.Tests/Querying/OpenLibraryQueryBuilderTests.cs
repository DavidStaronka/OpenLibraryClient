using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Core.Querying;

namespace OpenLibraryClient.Tests.Querying;

public class OpenLibraryQueryBuilderTests
{
    [Fact]
    public void Build_PrefersTitleAndAuthorOverKeywordsAndRawQuery()
    {
        var extraction = new ExtractionResult
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Keywords = ["desert"],
            Source = ExtractionSource.Deterministic,
            RawQuery = "Dune by Frank Herbert"
        };

        Assert.Equal("Dune Frank Herbert", OpenLibraryQueryBuilder.Build(extraction));
    }

    [Fact]
    public void Build_FallsBackToKeywordsWhenNoTitleOrAuthor()
    {
        var extraction = new ExtractionResult
        {
            Title = null,
            Author = null,
            Keywords = ["desert", "planet"],
            Source = ExtractionSource.Deterministic,
            RawQuery = "something about a desert planet"
        };

        Assert.Equal("desert planet", OpenLibraryQueryBuilder.Build(extraction));
    }

    [Fact]
    public void Build_FallsBackToRawQueryWhenNothingElseAvailable()
    {
        var extraction = new ExtractionResult
        {
            Title = null,
            Author = null,
            Keywords = [],
            Source = ExtractionSource.Deterministic,
            RawQuery = "totally unstructured text"
        };

        Assert.Equal("totally unstructured text", OpenLibraryQueryBuilder.Build(extraction));
    }

    [Fact]
    public void BuildTitleOnly_ReturnsNullWhenNoTitle()
    {
        var extraction = new ExtractionResult
        {
            Title = null,
            Source = ExtractionSource.Deterministic,
            RawQuery = "x"
        };

        Assert.Null(OpenLibraryQueryBuilder.BuildTitleOnly(extraction));
    }

    [Fact]
    public void BuildKeywordsOnly_ReturnsNullWhenNoKeywords()
    {
        var extraction = new ExtractionResult
        {
            Keywords = [],
            Source = ExtractionSource.Deterministic,
            RawQuery = "x"
        };

        Assert.Null(OpenLibraryQueryBuilder.BuildKeywordsOnly(extraction));
    }
}
