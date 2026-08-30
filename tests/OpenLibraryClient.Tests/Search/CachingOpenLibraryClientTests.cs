using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.OpenLibrary;

namespace OpenLibraryClient.Tests.Search;

public class CachingOpenLibraryClientTests
{
    private static CachingOpenLibraryClient CreateClient(Mock<IOpenLibraryClient> inner, IMemoryCache? cache = null, int cacheDurationMinutes = 20) =>
        new(
            inner.Object,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new OpenLibraryCacheOptions { CacheDurationMinutes = cacheDurationMinutes }));

    private static OpenLibraryDoc MakeDoc(string key = "/works/OL1") => new()
    {
        Key = key,
        Title = "Dune",
        AuthorNames = ["Frank Herbert"]
    };

    [Fact]
    public async Task SearchAsync_SecondCallWithSameQuery_ReturnsCachedResultWithoutCallingInner()
    {
        var inner = new Mock<IOpenLibraryClient>();
        inner.Setup(c => c.SearchAsync("dune", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeDoc()]);

        var client = CreateClient(inner);

        var first = await client.SearchAsync("dune");
        var second = await client.SearchAsync("dune");

        Assert.Single(first);
        Assert.Single(second);
        inner.Verify(c => c.SearchAsync("dune", 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_QueryDiffersOnlyByCaseOrWhitespace_HitsCache()
    {
        var inner = new Mock<IOpenLibraryClient>();
        inner.Setup(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeDoc()]);

        var client = CreateClient(inner);

        await client.SearchAsync("Dune");
        await client.SearchAsync("  dune  ");

        inner.Verify(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_EmptyResult_IsCachedJustLikeANonEmptyResult()
    {
        var inner = new Mock<IOpenLibraryClient>();
        inner.Setup(c => c.SearchAsync("some garbage query", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = CreateClient(inner);

        var first = await client.SearchAsync("some garbage query");
        var second = await client.SearchAsync("some garbage query");

        Assert.Empty(first);
        Assert.Empty(second);
        inner.Verify(c => c.SearchAsync("some garbage query", 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_DifferentLimit_IsTreatedAsADistinctCacheEntry()
    {
        var inner = new Mock<IOpenLibraryClient>();
        inner.Setup(c => c.SearchAsync("dune", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeDoc()]);

        var client = CreateClient(inner);

        await client.SearchAsync("dune", limit: 10);
        await client.SearchAsync("dune", limit: 20);

        inner.Verify(c => c.SearchAsync("dune", 10, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(c => c.SearchAsync("dune", 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_BlankQuery_ReturnsEmptyWithoutCallingInner()
    {
        var inner = new Mock<IOpenLibraryClient>();
        var client = CreateClient(inner);

        var result = await client.SearchAsync("   ");

        Assert.Empty(result);
        inner.Verify(c => c.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
