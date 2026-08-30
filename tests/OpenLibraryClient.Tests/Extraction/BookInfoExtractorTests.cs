using Moq;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.Extraction;

namespace OpenLibraryClient.Tests.Extraction;

public class BookInfoExtractorTests
{
    private static ExtractionResult Deterministic(
        string? title, string? author, string[]? keywords = null, double confidence = 0.85) => new()
    {
        Title = title,
        Author = author,
        Keywords = keywords ?? [],
        Confidence = confidence,
        Source = ExtractionSource.Deterministic,
        RawQuery = "raw"
    };

    private static ExtractionResult Llm(string? title = "Dune", string? author = "Frank Herbert") => new()
    {
        Title = title,
        Author = author,
        Keywords = [],
        Confidence = 0.9,
        Source = ExtractionSource.Llm,
        RawQuery = "raw"
    };

    private static (BookInfoExtractor Extractor, Mock<IDeterministicParser> Parser, Mock<ILlmBookInfoExtractor> Llm) CreateSut(
        double confidenceThreshold = 0.65)
    {
        var parser = new Mock<IDeterministicParser>();
        var llm = new Mock<ILlmBookInfoExtractor>();
        var extractor = new BookInfoExtractor(parser.Object, llm.Object, confidenceThreshold);
        return (extractor, parser, llm);
    }

    [Fact]
    public async Task ExtractAsync_HighConfidenceDeterministicMatch_ReturnsDeterministicWithoutCallingLlm()
    {
        var (extractor, parser, llm) = CreateSut();
        parser.Setup(p => p.Parse(It.IsAny<string>())).Returns(Deterministic("Dune", "Frank Herbert", confidence: 0.85));

        var result = await extractor.ExtractAsync("Dune by Frank Herbert");

        Assert.Equal(ExtractionSource.Deterministic, result.Source);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("dune")]
    [InlineData("dune frank herbert")]
    public async Task ExtractAsync_UnstructuredInputWithNoSeparator_AlwaysFallsBackToLlm(string bookInfo)
    {
        // Simulates the DeterministicParser's fallback keyword-bag result for input with no
        // recognizable separator pattern. Even short inputs like a bare title now go straight to
        // the LLM rather than being tried against Open Library as a raw/keyword query first.
        var (extractor, parser, llm) = CreateSut();
        parser.Setup(p => p.Parse(bookInfo)).Returns(Deterministic(null, null, keywords: bookInfo.Split(' '), confidence: 0.2));
        llm.Setup(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>())).ReturnsAsync(Llm());

        var result = await extractor.ExtractAsync(bookInfo);

        Assert.Equal(ExtractionSource.Llm, result.Source);
        llm.Verify(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_LongInputWithNoSeparator_FallsBackToLlm()
    {
        const string bookInfo = "some scifi book about a giant sandworm desert planet dune spice by frank herbert i think";
        var (extractor, parser, llm) = CreateSut();
        parser.Setup(p => p.Parse(bookInfo)).Returns(Deterministic(null, null, keywords: ["scifi", "sandworm"], confidence: 0.2));
        llm.Setup(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>())).ReturnsAsync(Llm());

        var result = await extractor.ExtractAsync(bookInfo);

        Assert.Equal(ExtractionSource.Llm, result.Source);
        llm.Verify(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_LowConfidenceStructuredMatch_FallsBackToLlm()
    {
        // Has a title/author (structured match found), just below the confidence threshold -
        // an ambiguous separator match should still consult the LLM.
        const string bookInfo = "Title, Author";
        var (extractor, parser, llm) = CreateSut();
        parser.Setup(p => p.Parse(bookInfo)).Returns(Deterministic("Title", "Author", confidence: 0.55));
        llm.Setup(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>())).ReturnsAsync(Llm());

        var result = await extractor.ExtractAsync(bookInfo);

        Assert.Equal(ExtractionSource.Llm, result.Source);
        llm.Verify(l => l.ExtractAsync(bookInfo, It.IsAny<CancellationToken>()), Times.Once);
    }
}
