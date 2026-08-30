using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// LLM-backed extraction using Microsoft.Extensions.AI's provider-agnostic IChatClient with
/// structured JSON output (constrained to LlmExtractionDto's shape), so the model's response
/// always deserializes cleanly rather than requiring free-text JSON parsing.
///
/// On any failure (network error, auth error, malformed response) this degrades gracefully to a
/// zero-confidence empty result rather than throwing, so a flaky/unavailable LLM never crashes
/// the search pipeline - BookSearchService's fallback chain simply moves on to the next query.
/// </summary>
public sealed class LlmBookInfoExtractor(IChatClient chatClient, ILogger<LlmBookInfoExtractor> logger) : ILlmBookInfoExtractor
{
    private const string SystemPrompt = """
        You are an expert librarian assistant. Extract structured book information from a messy,
        free-text user query that may contain misspellings, poor formatting, or irrelevant words.
        Identify the book title, the author's name, and any other useful keywords (genre, series,
        plot details, publication era, etc).

        Rules:
        - If you cannot confidently identify a field, return null for it.
        - Correct obvious misspellings when identifying title/author (e.g. "Frnk Hrbert" becomes "Frank Herbert").
        - "confidence" is your own 0.0-1.0 estimate of how sure you are about the extracted title/author.
        - "explanation" is a short, one-to-two sentence rationale for why you extracted these specific
          fields from the query (e.g. what in the text indicated the title vs. the author vs. keywords).
        - Do not invent information that isn't implied by the query.
        """;

    public async Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            List<ChatMessage> messages =
            [
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, bookInfo)
            ];

            var response = await chatClient.GetResponseAsync<LlmExtractionDto>(messages, cancellationToken: cancellationToken);

            return MapToExtractionResult(response.Result, bookInfo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM extraction failed for query '{BookInfo}'; degrading to a zero-confidence result.", bookInfo);

            return new ExtractionResult
            {
                Title = null,
                Author = null,
                Keywords = [],
                Confidence = 0.0,
                Source = ExtractionSource.Llm,
                Explanation = "LLM extraction failed; degraded to a zero-confidence result",
                RawQuery = bookInfo
            };
        }
    }

    internal static ExtractionResult MapToExtractionResult(LlmExtractionDto dto, string rawQuery) => new()
    {
        Title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim(),
        Author = string.IsNullOrWhiteSpace(dto.Author) ? null : dto.Author.Trim(),
        Keywords = dto.Keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .Distinct()
            .ToList(),
        Confidence = Math.Clamp(dto.Confidence, 0.0, 1.0),
        Source = ExtractionSource.Llm,
        Explanation = string.IsNullOrWhiteSpace(dto.Explanation) ? null : dto.Explanation.Trim(),
        RawQuery = rawQuery
    };
}
