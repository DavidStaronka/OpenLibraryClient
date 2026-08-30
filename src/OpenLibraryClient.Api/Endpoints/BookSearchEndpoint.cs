using Microsoft.Extensions.Logging;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Api.Endpoints;

/// <summary>Maps GET /api/books/search to the search orchestration pipeline.</summary>
public static class BookSearchEndpoint
{
    /// <summary>
    /// Upper bound on the "bookInfo" query length. Generous for any realistic book description,
    /// while capping the cost of downstream regex/fuzzy-matching, Open Library, and (notably
    /// billable/rate-limited) LLM calls that a single request can trigger.
    /// </summary>
    public const int MaxBookInfoLength = 300;

    /// <summary>
    /// Name of the rate limiter policy (registered in Program.cs via AddRateLimiter) applied to
    /// this endpoint - each search can trigger a billable/rate-limited LLM call, so this guards
    /// against a single client driving unbounded cost or hammering the upstream services.
    /// </summary>
    public const string RateLimitPolicyName = "SearchPolicy";

    public static IEndpointRouteBuilder MapBookSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/books/search", HandleAsync)
            .WithName("SearchBooks")
            .RequireRateLimiting(RateLimitPolicyName);

        return app;
    }

    internal static async Task<IResult> HandleAsync(
        string? bookInfo,
        IBookSearchService searchService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bookInfo))
        {
            return Results.BadRequest(new { error = "Query parameter 'bookInfo' is required." });
        }

        if (bookInfo.Length > MaxBookInfoLength)
        {
            return Results.BadRequest(new
            {
                error = $"Query parameter 'bookInfo' must be {MaxBookInfoLength} characters or fewer."
            });
        }

        BookSearchResult searchResult;
        try
        {
            searchResult = await searchService.SearchAsync(bookInfo, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Book search failed for query '{BookInfo}'.", bookInfo);
            return Results.Problem(
                title: "Book search is temporarily unavailable.",
                detail: "The search could not be completed, possibly due to an upstream service issue. Please try again shortly.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var response = new BookSearchResponse
        {
            Query = bookInfo,
            Extraction = ExtractionSummary.From(searchResult.Extraction),
            Explanation = searchResult.Extraction.Explanation,
            Results = searchResult.Results.Select(RankedResultDto.From).ToList(),
            QueriesAttempted = searchResult.QueriesAttempted
        };

        return Results.Ok(response);
    }
}
