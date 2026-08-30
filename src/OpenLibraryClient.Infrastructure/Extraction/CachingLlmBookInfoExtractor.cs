using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// Caches LLM extraction results in-process, keyed by normalized raw query text (see
/// <see cref="LlmCacheOptions"/>). Failed/degraded extractions (the zero-confidence result
/// <see cref="LlmBookInfoExtractor"/> returns after retries are exhausted) are deliberately NOT
/// cached, so a transient LLM outage doesn't get "stuck" as a cached failure for the TTL -
/// only extractions with some positive confidence are considered cache-worthy.
/// </summary>
public sealed class CachingLlmBookInfoExtractor(
    ILlmBookInfoExtractor innerExtractor,
    IMemoryCache cache,
    IOptions<LlmCacheOptions> options) : ILlmBookInfoExtractor
{
    public async Task<ExtractionResult> ExtractAsync(string bookInfo, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"gemini:extract:{bookInfo.Trim().ToLowerInvariant()}";

        if (cache.TryGetValue(cacheKey, out ExtractionResult? cached) && cached is not null)
        {
            return cached;
        }

        var result = await innerExtractor.ExtractAsync(bookInfo, cancellationToken);

        if (result.Confidence > 0.0)
        {
            cache.Set(cacheKey, result, TimeSpan.FromHours(options.Value.CacheDurationHours));
        }

        return result;
    }
}
