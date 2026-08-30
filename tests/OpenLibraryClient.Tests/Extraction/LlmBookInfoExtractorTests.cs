using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.Extraction;
using Polly;
using Polly.Retry;

namespace OpenLibraryClient.Tests.Extraction;

public class LlmBookInfoExtractorTests
{
    private static LlmBookInfoExtractor CreateExtractor(Mock<IChatClient> chatClient, ResiliencePipeline? resiliencePipeline = null) =>
        new(chatClient.Object, NullLogger<LlmBookInfoExtractor>.Instance, resiliencePipeline);

    private static void SetupChatResponse(Mock<IChatClient> chatClient, string jsonContent)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonContent));

        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    [Fact]
    public async Task ExtractAsync_ValidStructuredResponse_MapsToExtractionResult()
    {
        var chatClient = new Mock<IChatClient>();
        SetupChatResponse(chatClient, """{"title":"Dune","author":"Frank Herbert","keywords":["sci-fi","desert"],"explanation":"The query names a title and an author separated by 'by'."}""");

        var extractor = CreateExtractor(chatClient);
        var result = await extractor.ExtractAsync("dune by frnk hrbert");

        Assert.Equal("Dune", result.Title);
        Assert.Equal("Frank Herbert", result.Author);
        Assert.Equal(["sci-fi", "desert"], result.Keywords);
        Assert.False(result.IsDegraded);
        Assert.Equal(ExtractionSource.Llm, result.Source);
        Assert.Equal("dune by frnk hrbert", result.RawQuery);
        Assert.Equal("The query names a title and an author separated by 'by'.", result.Explanation);
    }

    [Fact]
    public async Task ExtractAsync_ChatClientThrows_DegradesToEmptyResultInsteadOfThrowing()
    {
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated network failure"));

        var extractor = CreateExtractor(chatClient);
        var result = await extractor.ExtractAsync("dune by frnk hrbert");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.Empty(result.Keywords);
        Assert.True(result.IsDegraded);
        Assert.Equal(ExtractionSource.Llm, result.Source);
        Assert.NotNull(result.Explanation);

        // No resilience pipeline supplied - a single failed attempt should degrade immediately
        // rather than retry, since retry behavior is opt-in via the pipeline parameter.
        chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_TransientFailureWithResiliencePipeline_RetriesAndEventuallySucceeds()
    {
        var chatClient = new Mock<IChatClient>();
        var callCount = 0;
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new HttpRequestException("simulated transient network failure");
                }

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    """{"title":"Dune","author":"Frank Herbert","keywords":[]}""")));
            });

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();

        var extractor = CreateExtractor(chatClient, pipeline);
        var result = await extractor.ExtractAsync("dune by frnk hrbert");

        Assert.Equal("Dune", result.Title);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExtractAsync_NonTransientFailureWithResiliencePipeline_DoesNotRetryAndDegrades()
    {
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated non-transient failure"));

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();

        var extractor = CreateExtractor(chatClient, pipeline);
        var result = await extractor.ExtractAsync("dune by frnk hrbert");

        Assert.True(result.IsDegraded);
        chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_NotConfigured_SkipsChatClientAndReturnsDegradedResult()
    {
        var chatClient = new Mock<IChatClient>();

        var extractor = new LlmBookInfoExtractor(
            chatClient.Object,
            NullLogger<LlmBookInfoExtractor>.Instance,
            resiliencePipeline: null,
            isConfigured: false);

        var result = await extractor.ExtractAsync("dune by frank herbert");

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.True(result.IsDegraded);
        Assert.Equal(ExtractionSource.Llm, result.Source);
        Assert.Contains("no gemini api key configured", result.Explanation, StringComparison.OrdinalIgnoreCase);
        chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Dune", "Dune")]
    [InlineData("  Dune  ", "Dune")]
    public void MapToExtractionResult_NormalizesBlankTitleToNull(string? rawTitle, string? expectedTitle)
    {
        var dto = new LlmExtractionDto { Title = rawTitle, Author = null, Keywords = [] };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(expectedTitle, result.Title);
    }

    [Fact]
    public void MapToExtractionResult_FiltersBlankKeywordsAndDeduplicatesCaseInsensitively()
    {
        var dto = new LlmExtractionDto
        {
            Title = null,
            Author = null,
            Keywords = ["Desert", "desert", "  ", "", "Planet"]
        };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(["desert", "planet"], result.Keywords);
    }

    [Fact]
    public void MapToExtractionResult_TruncatesOverlyLongTitleAuthorAndExplanation()
    {
        var longValue = new string('a', 500);
        var dto = new LlmExtractionDto
        {
            Title = longValue,
            Author = longValue,
            Keywords = [],
            Explanation = longValue
        };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(LlmBookInfoExtractor.MaxFieldLength, result.Title!.Length);
        Assert.Equal(LlmBookInfoExtractor.MaxFieldLength, result.Author!.Length);
        Assert.Equal(LlmBookInfoExtractor.MaxFieldLength, result.Explanation!.Length);
    }

    [Fact]
    public void MapToExtractionResult_CapsKeywordCountAndTruncatesOverlyLongKeywords()
    {
        var manyKeywords = Enumerable.Range(0, 50).Select(i => $"keyword{i}").ToList();
        var dto = new LlmExtractionDto
        {
            Title = null,
            Author = null,
            Keywords = [.. manyKeywords, new string('b', 500)]
        };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(LlmBookInfoExtractor.MaxKeywords, result.Keywords.Count);
        Assert.All(result.Keywords, k => Assert.True(k.Length <= LlmBookInfoExtractor.MaxFieldLength));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("The title follows 'by'.", "The title follows 'by'.")]
    [InlineData("  Trimmed explanation.  ", "Trimmed explanation.")]
    public void MapToExtractionResult_NormalizesBlankExplanationToNull(string? rawExplanation, string? expectedExplanation)
    {
        var dto = new LlmExtractionDto { Title = null, Author = null, Keywords = [], Explanation = rawExplanation };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(expectedExplanation, result.Explanation);
    }
}
