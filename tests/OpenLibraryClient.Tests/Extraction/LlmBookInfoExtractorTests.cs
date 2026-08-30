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
        SetupChatResponse(chatClient, """{"title":"Dune","author":"Frank Herbert","keywords":["sci-fi","desert"],"confidence":0.9,"explanation":"The query names a title and an author separated by 'by'."}""");

        var extractor = CreateExtractor(chatClient);
        var result = await extractor.ExtractAsync("dune by frnk hrbert");

        Assert.Equal("Dune", result.Title);
        Assert.Equal("Frank Herbert", result.Author);
        Assert.Equal(["sci-fi", "desert"], result.Keywords);
        Assert.Equal(0.9, result.Confidence);
        Assert.Equal(ExtractionSource.Llm, result.Source);
        Assert.Equal("dune by frnk hrbert", result.RawQuery);
        Assert.Equal("The query names a title and an author separated by 'by'.", result.Explanation);
    }

    [Fact]
    public async Task ExtractAsync_ChatClientThrows_DegradesToZeroConfidenceResultInsteadOfThrowing()
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
        Assert.Equal(0.0, result.Confidence);
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
                    """{"title":"Dune","author":"Frank Herbert","keywords":[],"confidence":0.9}""")));
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

        Assert.Equal(0.0, result.Confidence);
        chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Dune", "Dune")]
    [InlineData("  Dune  ", "Dune")]
    public void MapToExtractionResult_NormalizesBlankTitleToNull(string? rawTitle, string? expectedTitle)
    {
        var dto = new LlmExtractionDto { Title = rawTitle, Author = null, Keywords = [], Confidence = 0.5 };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(expectedTitle, result.Title);
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.42, 0.42)]
    public void MapToExtractionResult_ClampsConfidenceTo0To1Range(double rawConfidence, double expectedConfidence)
    {
        var dto = new LlmExtractionDto { Title = null, Author = null, Keywords = [], Confidence = rawConfidence };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(expectedConfidence, result.Confidence);
    }

    [Fact]
    public void MapToExtractionResult_FiltersBlankKeywordsAndDeduplicatesCaseInsensitively()
    {
        var dto = new LlmExtractionDto
        {
            Title = null,
            Author = null,
            Keywords = ["Desert", "desert", "  ", "", "Planet"],
            Confidence = 0.5
        };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(["desert", "planet"], result.Keywords);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("The title follows 'by'.", "The title follows 'by'.")]
    [InlineData("  Trimmed explanation.  ", "Trimmed explanation.")]
    public void MapToExtractionResult_NormalizesBlankExplanationToNull(string? rawExplanation, string? expectedExplanation)
    {
        var dto = new LlmExtractionDto { Title = null, Author = null, Keywords = [], Confidence = 0.5, Explanation = rawExplanation };

        var result = LlmBookInfoExtractor.MapToExtractionResult(dto, "raw");

        Assert.Equal(expectedExplanation, result.Explanation);
    }
}
