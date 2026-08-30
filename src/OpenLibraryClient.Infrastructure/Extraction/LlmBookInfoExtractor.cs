using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Infrastructure.Search;
using Polly;

namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// LLM-backed extraction using Microsoft.Extensions.AI's provider-agnostic IChatClient with
/// structured JSON output (constrained to LlmExtractionDto's shape), so the model's response
/// always deserializes cleanly rather than requiring free-text JSON parsing.
///
/// Transient failures (timeouts, 429, 5xx) are retried a bounded number of times via the
/// supplied <paramref name="resiliencePipeline"/> (built in Program.cs from "Gemini:Resilience"
/// configuration; defaults to no retry - e.g. in tests - when not supplied). On any failure that
/// survives retries (network error, auth error, malformed response) this degrades gracefully to
/// an empty, <see cref="ExtractionResult.IsDegraded"/> result rather than throwing, so a
/// flaky/unavailable LLM never crashes the search pipeline - BookSearchService's fallback chain
/// simply moves on to the next query.
///
/// When no real Gemini API key is configured (<paramref name="isConfigured"/> is false, computed
/// in Program.cs from the same check used for its startup warning), calls are short-circuited
/// before ever touching the (doomed-to-fail) chat client, and the resulting degraded result
/// carries a distinct explanation/metric reason ("not-configured") so this case is never
/// conflated with a genuine transient outage in logs/dashboards.
/// </summary>
public sealed class LlmBookInfoExtractor(
    IChatClient chatClient,
    ILogger<LlmBookInfoExtractor> logger,
    ResiliencePipeline? resiliencePipeline = null,
    bool isConfigured = true,
    BookSearchMetrics? metrics = null) : ILlmBookInfoExtractor
{
    private readonly ResiliencePipeline _resiliencePipeline = resiliencePipeline ?? ResiliencePipeline.Empty;

    private const string SystemPrompt = """
        You are an expert librarian assistant. Extract structured book information from a messy,
        free-text user query that may contain misspellings, poor formatting, or irrelevant words.
        Identify the book title, the author's name, and any other useful keywords (genre, series,
        plot details, publication era, etc).

        Rules:
        - If you cannot confidently identify a field, return null for it.
        - Correct obvious misspellings when identifying title/author (e.g. "Frnk Hrbert" becomes "Frank Herbert").
        - "explanation" is a short, one-to-two sentence rationale for why you extracted these specific
          fields from the query (e.g. what in the text indicated the title vs. the author vs. keywords).
        - Do not invent information that isn't implied by the query.
        - Attempt to offer the title and author if it is clear that is the book the user is describing, 
          even if the user didn't explicitly say "title" or "author" or one of the standard templates.
        """;

    public async Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        if (!isConfigured)
        {
            logger.LogDebug("LLM extraction skipped for query '{BookInfo}': no Gemini API key configured.", bookInfo);
            metrics?.RecordLlmExtractionOutcome("not-configured");

            return new ExtractionResult
            {
                Title = null,
                Author = null,
                Keywords = [],
                IsDegraded = true,
                Source = ExtractionSource.Llm,
                Explanation = "LLM extraction skipped; no Gemini API key configured",
                RawQuery = bookInfo
            };
        }

        try
        {
            List<ChatMessage> messages =
            [
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, bookInfo)
            ];

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await chatClient.GetResponseAsync<LlmExtractionDto>(messages, cancellationToken: ct),
                cancellationToken);

            metrics?.RecordLlmExtractionOutcome("success");
            return MapToExtractionResult(response.Result, bookInfo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM extraction failed for query '{BookInfo}'; degrading to an empty result.", bookInfo);
            metrics?.RecordLlmExtractionOutcome("failure");

            return new ExtractionResult
            {
                Title = null,
                Author = null,
                Keywords = [],
                IsDegraded = true,
                Source = ExtractionSource.Llm,
                Explanation = "LLM extraction failed; degraded to an empty result",
                RawQuery = bookInfo
            };
        }
    }

    /// <summary>
    /// Upper bound on the length of any single free-text field (Title/Author/Explanation) the
    /// LLM returns. Guards against a pathological/misbehaving model response bloating the
    /// response payload; generous for any realistic title, author name, or explanation.
    /// </summary>
    internal const int MaxFieldLength = 200;

    /// <summary>Upper bound on the number of keywords kept from the LLM's response.</summary>
    internal const int MaxKeywords = 20;

    internal static ExtractionResult MapToExtractionResult(LlmExtractionDto dto, string rawQuery) => new()
    {
        Title = TruncateOrNull(dto.Title),
        Author = TruncateOrNull(dto.Author),
        Keywords = dto.Keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Truncate(k.Trim().ToLowerInvariant(), MaxFieldLength))
            .Distinct()
            .Take(MaxKeywords)
            .ToList(),
        Source = ExtractionSource.Llm,
        Explanation = TruncateOrNull(dto.Explanation),
        RawQuery = rawQuery
    };

    private static string? TruncateOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), MaxFieldLength);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
