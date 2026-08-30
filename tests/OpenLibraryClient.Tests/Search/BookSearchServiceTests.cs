using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;
using Moq;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.Search;

namespace OpenLibraryClient.Tests.Search;

public class BookSearchServiceTests
{
    // BookSearchService now records metrics via BookSearchMetrics; tests don't assert on metrics
    // output, so a real (but throwaway) meter is enough to satisfy the constructor.
    private static BookSearchMetrics NewMetrics() => new(new TestMeterFactory());

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private static ExtractionResult Deterministic(string? title, string? author, string[]? keywords = null, string rawQuery = "raw query", double confidence = 0.85) => new()
    {
        Title = title,
        Author = author,
        Keywords = keywords ?? [],
        Confidence = confidence,
        Source = ExtractionSource.Deterministic,
        RawQuery = rawQuery
    };

    private static ExtractionResult Llm(string? title, string? author, string[]? keywords = null, string rawQuery = "raw query", double confidence = 0.3) => new()
    {
        Title = title,
        Author = author,
        Keywords = keywords ?? [],
        Confidence = confidence,
        Source = ExtractionSource.Llm,
        RawQuery = rawQuery
    };

    private static OpenLibraryDoc Doc(string key = "/works/OL1") => new()
    {
        Key = key,
        Title = "Some Title",
        AuthorNames = ["Some Author"],
        EditionCount = 10
    };

    private static (Mock<IBookInfoExtractor> Extractor, Mock<ILlmBookInfoExtractor> Llm, Mock<IOpenLibraryClient> OpenLibrary, Mock<IRelevanceRanker> Ranker) MockSet()
    {
        var extractor = new Mock<IBookInfoExtractor>();
        var llm = new Mock<ILlmBookInfoExtractor>();
        var openLibrary = new Mock<IOpenLibraryClient>();
        var ranker = new Mock<IRelevanceRanker>();

        // Default: ranker just echoes candidates back as empty-scored ranked results, keeping
        // these tests focused on the retry/orchestration logic rather than scoring.
        ranker
            .Setup(r => r.Rank(It.IsAny<ExtractionResult>(), It.IsAny<IReadOnlyList<OpenLibraryDoc>>()))
            .Returns((ExtractionResult _, IReadOnlyList<OpenLibraryDoc> docs) =>
                docs.Select(d => new RankedResult
                {
                    Doc = d,
                    Score = 1.0,
                    Breakdown = new ScoreBreakdown { TitleSimilarity = 1, AuthorSimilarity = 1, KeywordOverlap = 1, PopularityNorm = 1, ExtractionConfidence = 1 },
                    Explanation = "test explanation"
                }).ToList());

        return (extractor, llm, openLibrary, ranker);
    }

    [Fact]
    public async Task SearchAsync_FirstAttemptSucceeds_MakesOnlyOneQueryAndNeverCallsLlm()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var extraction = Deterministic("Dune", "Frank Herbert");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);
        openLibrary.Setup(c => c.SearchAsync("Dune Frank Herbert", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Doc()]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("Dune by Frank Herbert");

        Assert.Single(result.QueriesAttempted);
        Assert.Equal("Dune Frank Herbert", result.QueriesAttempted[0]);
        Assert.Single(result.Results);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        openLibrary.Verify(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_DeterministicMatchFoundButNotExact_FallsBackToLlmAndPrefersItsResults()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        // "Frank H" is a plausible-looking but incomplete author guess (e.g. from "Dune - Frank H").
        var deterministicExtraction = Deterministic("Dune", "Frank H");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(deterministicExtraction);

        var partialMatchDoc = Doc("/works/OL1");
        var exactMatchDoc = Doc("/works/OL2");

        openLibrary.Setup(c => c.SearchAsync("Dune Frank H", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([partialMatchDoc]);

        var llmExtraction = Llm("Dune", "Frank Herbert");
        llm.Setup(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(llmExtraction);
        openLibrary.Setup(c => c.SearchAsync("Dune Frank Herbert", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([exactMatchDoc]);

        // Override the MockSet default (which scores everything a flat 1.0): the deterministic
        // candidate is only a partial author match, the LLM's candidate is an exact match.
        ranker
            .Setup(r => r.Rank(deterministicExtraction, It.Is<IReadOnlyList<OpenLibraryDoc>>(docs => docs.Contains(partialMatchDoc))))
            .Returns([new RankedResult
            {
                Doc = partialMatchDoc,
                Score = 0.7,
                Breakdown = new ScoreBreakdown { TitleSimilarity = 1.0, AuthorSimilarity = 0.6, KeywordOverlap = 0, PopularityNorm = 0, ExtractionConfidence = 0.85 },
                Explanation = "partial match"
            }]);
        ranker
            .Setup(r => r.Rank(llmExtraction, It.Is<IReadOnlyList<OpenLibraryDoc>>(docs => docs.Contains(exactMatchDoc))))
            .Returns([new RankedResult
            {
                Doc = exactMatchDoc,
                Score = 1.0,
                Breakdown = new ScoreBreakdown { TitleSimilarity = 1.0, AuthorSimilarity = 1.0, KeywordOverlap = 0, PopularityNorm = 0, ExtractionConfidence = 0.3 },
                Explanation = "exact match"
            }]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("Dune - Frank H");

        Assert.Equal(ExtractionSource.Llm, result.Extraction.Source);
        Assert.Single(result.Results);
        Assert.Equal("/works/OL2", result.Results[0].Doc.Key);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_DeterministicMatchNotExactAndLlmFindsNothing_KeepsDeterministicCandidates()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var deterministicExtraction = Deterministic("Dune", "Frankie"); // wrong author guess
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(deterministicExtraction);

        var candidateDoc = Doc("/works/OL1");
        openLibrary.Setup(c => c.SearchAsync("Dune Frankie", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([candidateDoc]);

        var llmExtraction = Llm("Dune", "Frank Herbert");
        llm.Setup(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(llmExtraction);
        openLibrary.Setup(c => c.SearchAsync("Dune Frank Herbert", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        openLibrary.Setup(c => c.SearchAsync("Dune", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        ranker
            .Setup(r => r.Rank(deterministicExtraction, It.Is<IReadOnlyList<OpenLibraryDoc>>(docs => docs.Contains(candidateDoc))))
            .Returns([new RankedResult
            {
                Doc = candidateDoc,
                Score = 0.5,
                Breakdown = new ScoreBreakdown { TitleSimilarity = 1.0, AuthorSimilarity = 0.3, KeywordOverlap = 0, PopularityNorm = 0, ExtractionConfidence = 0.85 },
                Explanation = "partial match"
            }]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("Dune - Frankie");

        // The LLM was consulted (since the deterministic match wasn't validated as exact), but
        // it found nothing, so the deterministic candidates/extraction are kept as the best
        // available guess rather than being discarded.
        Assert.Equal(ExtractionSource.Deterministic, result.Extraction.Source);
        Assert.Single(result.Results);
        Assert.Equal("/works/OL1", result.Results[0].Doc.Key);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_FullQueryZero_TitleOnlyRetrySucceeds_SkipsLlm()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var extraction = Deterministic("Dune", "Frnk Hrbert"); // misspelled author
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);

        openLibrary.Setup(c => c.SearchAsync("Dune Frnk Hrbert", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        openLibrary.Setup(c => c.SearchAsync("Dune", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Doc()]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("Dune by Frnk Hrbert");

        Assert.Equal(["Dune Frnk Hrbert", "Dune"], result.QueriesAttempted);
        Assert.Single(result.Results);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(ExtractionSource.Deterministic, result.Extraction.Source);
    }

    [Fact]
    public async Task SearchAsync_AllDeterministicAttemptsZero_FallsBackToLlmAndUsesItsExtraction()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var deterministicExtraction = Deterministic("Dun", "Frnk Hrbert", rawQuery: "dun by frnk hrbert");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(deterministicExtraction);

        var llmExtraction = Llm("Dune", "Frank Herbert", rawQuery: "dun by frnk hrbert");
        llm.Setup(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(llmExtraction);

        openLibrary.Setup(c => c.SearchAsync("Dun Frnk Hrbert", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        openLibrary.Setup(c => c.SearchAsync("Dun", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        openLibrary.Setup(c => c.SearchAsync("Dune Frank Herbert", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Doc()]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("dun by frnk hrbert");

        Assert.Equal(["Dun Frnk Hrbert", "Dun", "Dune Frank Herbert"], result.QueriesAttempted);
        Assert.Single(result.Results);
        Assert.Equal(ExtractionSource.Llm, result.Extraction.Source);
        Assert.Equal("Dune", result.Extraction.Title);
        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ExtractionAlreadyFromLlm_NeverCallsLlmAgain()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var extraction = Llm(null, null, keywords: ["desert", "planet"], rawQuery: "desert planet book");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);

        openLibrary.Setup(c => c.SearchAsync("desert planet", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Doc()]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("desert planet book");

        llm.Verify(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task SearchAsync_AllAttemptsExhausted_ReturnsEmptyResultsWithoutThrowing()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        var extraction = Deterministic("Xyz", "Abc", keywords: ["nomatch"], rawQuery: "xyz by abc nomatch");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);
        llm.Setup(l => l.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Llm("Xyz", "Abc", keywords: ["nomatch"], rawQuery: "xyz by abc nomatch"));

        openLibrary.Setup(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("xyz by abc nomatch");

        Assert.Empty(result.Results);
        Assert.NotEmpty(result.QueriesAttempted);
    }

    [Fact]
    public async Task SearchAsync_DuplicateQueryAcrossFallbackSteps_IsOnlyQueriedOnce()
    {
        var (extractor, llm, openLibrary, ranker) = MockSet();
        // Keywords-only and raw query happen to produce the exact same string. Using an
        // already-Llm-sourced extraction so the LLM retry branch doesn't fire here - this test
        // targets the keywords-only/raw-query dedupe specifically.
        var extraction = Llm(title: null, author: null, keywords: ["same", "text"], rawQuery: "same text");
        extractor.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(extraction);

        openLibrary.Setup(c => c.SearchAsync("same text", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var service = new BookSearchService(extractor.Object, llm.Object, openLibrary.Object, ranker.Object, NewMetrics());
        var result = await service.SearchAsync("same text");

        Assert.Equal(["same text"], result.QueriesAttempted);
        openLibrary.Verify(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
