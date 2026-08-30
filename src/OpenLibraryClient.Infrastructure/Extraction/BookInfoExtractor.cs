using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// Implements the confidence-gate policy: try the deterministic parser first; only fall back to
/// the (slower, costlier) LLM extractor when deterministic confidence is below the configured
/// threshold or no title/author could be identified at all.
/// </summary>
public sealed class BookInfoExtractor(
    IDeterministicParser deterministicParser,
    ILlmBookInfoExtractor llmExtractor,
    double confidenceThreshold = 0.65) : IBookInfoExtractor
{
    public async Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        var deterministicResult = deterministicParser.Parse(bookInfo);

        if (deterministicResult.HasTitleOrAuthor && deterministicResult.Confidence >= confidenceThreshold)
        {
            return deterministicResult;
        }

        // Either no recognizable title/author separator (an unstructured keyword bag) or a
        // low-confidence structured match: in both cases, hand off to the LLM rather than
        // querying Open Library directly with the deterministic result. Regexes can't reliably
        // identify title/author in free-form text, so the LLM's extraction is worth its cost
        // here regardless of how short the input is.
        return await llmExtractor.ExtractAsync(bookInfo, cancellationToken);
    }
}
