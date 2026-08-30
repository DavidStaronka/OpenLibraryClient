using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.Extraction;

namespace OpenLibraryClient.Tests.Extraction;

public class CachingLlmBookInfoExtractorTests
{
    private static CachingLlmBookInfoExtractor CreateExtractor(Mock<ILlmBookInfoExtractor> inner, IMemoryCache? cache = null) =>
        new(
            inner.Object,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new LlmCacheOptions { CacheDurationHours = 24 }));

    private static ExtractionResult MakeResult(string rawQuery, bool isDegraded = false) => new()
    {
        Title = "Dune",
        Author = "Frank Herbert",
        Keywords = [],
        IsDegraded = isDegraded,
        Source = ExtractionSource.Llm,
        Explanation = "explanation",
        RawQuery = rawQuery
    };

    [Fact]
    public async Task ExtractAsync_SecondCallWithSameQuery_ReturnsCachedResultWithoutCallingInner()
    {
        var inner = new Mock<ILlmBookInfoExtractor>();
        inner.Setup(e => e.ExtractAsync("dune by frank herbert", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("dune by frank herbert"));

        var extractor = CreateExtractor(inner);

        var first = await extractor.ExtractAsync("dune by frank herbert");
        var second = await extractor.ExtractAsync("dune by frank herbert");

        Assert.Equal("Dune", first.Title);
        Assert.Equal("Dune", second.Title);
        inner.Verify(e => e.ExtractAsync("dune by frank herbert", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_QueryDiffersOnlyByCaseOrWhitespace_HitsCache()
    {
        var inner = new Mock<ILlmBookInfoExtractor>();
        inner.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, CancellationToken _) => MakeResult(q));

        var extractor = CreateExtractor(inner);

        await extractor.ExtractAsync("Dune by Frank Herbert");
        await extractor.ExtractAsync("  dune by frank herbert  ");

        inner.Verify(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_DegradedResult_IsNotCached()
    {
        var inner = new Mock<ILlmBookInfoExtractor>();
        inner.Setup(e => e.ExtractAsync("flaky query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("flaky query", isDegraded: true));

        var extractor = CreateExtractor(inner);

        await extractor.ExtractAsync("flaky query");
        await extractor.ExtractAsync("flaky query");

        // Not cached, so the (degraded/failed) inner extractor is called again each time -
        // ensures a transient LLM outage doesn't get "stuck" as a cached failure.
        inner.Verify(e => e.ExtractAsync("flaky query", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
