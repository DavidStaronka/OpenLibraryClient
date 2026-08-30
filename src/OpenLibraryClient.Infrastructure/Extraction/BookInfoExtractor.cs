using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// Implements the extraction policy: try the deterministic parser first; only fall back to the
/// (slower, costlier) LLM extractor when it couldn't identify a title/author at all. Since
/// <see cref="IDeterministicParser"/> only ever recognizes unambiguous separators (anything
/// weaker falls into its unstructured-keyword-bag case), a structured match from it is always
/// trusted outright - there is no partial-confidence middle ground to gate on.
/// </summary>
public sealed class BookInfoExtractor(
    IDeterministicParser deterministicParser,
    ILlmBookInfoExtractor llmExtractor) : IBookInfoExtractor
{
    public async Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        var deterministicResult = deterministicParser.Parse(bookInfo);

        if (deterministicResult.HasTitleOrAuthor)
        {
            return deterministicResult;
        }

        // No recognizable title/author separator (an unstructured keyword bag): hand off to the
        // LLM rather than querying Open Library directly with the deterministic result. Regexes
        // can't reliably identify title/author in free-form text, so the LLM's extraction is
        // worth its cost here regardless of how short the input is.
        return await llmExtractor.ExtractAsync(bookInfo, cancellationToken);
    }
}
