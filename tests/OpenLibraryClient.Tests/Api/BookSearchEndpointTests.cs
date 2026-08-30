using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenLibraryClient.Api.Endpoints;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Tests.Api;

public class BookSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BookSearchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_MissingBookInfo_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/books/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_BookInfoExceedsMaxLength_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var tooLong = new string('a', BookSearchEndpoint.MaxBookInfoLength + 1);

        var response = await client.GetAsync($"/api/books/search?bookInfo={tooLong}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_SearchServiceThrows_ReturnsBadGateway()
    {
        var mockSearchService = new Mock<IBookSearchService>();
        mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Open Library is unavailable"));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockSearchService.Object);
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/books/search?bookInfo=Dune");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Search_ValidBookInfo_ReturnsRankedResultsAndQueriesAttemptedFromMockedService()
    {
        var extraction = new ExtractionResult
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Keywords = [],
            Confidence = 0.85,
            Source = ExtractionSource.Deterministic,
            Explanation = "input clear enough for deterministic match",
            RawQuery = "Dune by Frank Herbert"
        };

        var doc = new OpenLibraryDoc
        {
            Key = "/works/OL1",
            Title = "Dune",
            AuthorNames = ["Frank Herbert"],
            EditionCount = 50,
            Subjects = ["Science fiction"]
        };

        var searchResult = new BookSearchResult
        {
            Extraction = extraction,
            Results =
            [
                new RankedResult
                {
                    Doc = doc,
                    Score = 0.9,
                    Breakdown = new ScoreBreakdown
                    {
                        TitleSimilarity = 1.0,
                        AuthorSimilarity = 1.0,
                        KeywordOverlap = 0.0,
                        PopularityNorm = 1.0,
                        ExtractionConfidence = 0.85
                    },
                    Explanation = "Title is a very close match; author is a very close match."
                }
            ],
            QueriesAttempted = ["Dune Frank Herbert"]
        };

        var mockSearchService = new Mock<IBookSearchService>();
        mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockSearchService.Object);
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/books/search?bookInfo=Dune%20by%20Frank%20Herbert");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BookSearchResponse>();

        Assert.NotNull(body);
        Assert.Equal("Dune", body!.Extraction.Title);
        Assert.Equal("input clear enough for deterministic match", body.Explanation);
        Assert.Single(body.Results);
        Assert.Equal("/works/OL1", body.Results[0].Key);
        Assert.Equal("Title is a very close match; author is a very close match.", body.Results[0].Explanation);
        Assert.Equal(["Dune Frank Herbert"], body.QueriesAttempted);
    }

    [Fact]
    public async Task Search_ExceedsRateLimit_ReturnsTooManyRequests()
    {
        var mockSearchService = new Mock<IBookSearchService>();
        mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookSearchResult
            {
                Extraction = new ExtractionResult
                {
                    Title = "Dune",
                    Author = null,
                    Keywords = [],
                    Confidence = 0.85,
                    Source = ExtractionSource.Deterministic,
                    RawQuery = "Dune"
                },
                Results = [],
                QueriesAttempted = ["Dune"]
            });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            // Tight limit so the test can trip it deterministically without many requests.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "1",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:QueueLimit"] = "0"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockSearchService.Object);
            });
        }).CreateClient();

        var first = await client.GetAsync("/api/books/search?bookInfo=Dune");
        var second = await client.GetAsync("/api/books/search?bookInfo=Dune");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
