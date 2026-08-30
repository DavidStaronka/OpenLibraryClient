using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.OpenLibrary;

/// <summary>
/// Caches Open Library search results in-process, keyed by normalized query text + limit, to
/// reduce outbound calls for repeated/popular queries (including <c>BookSearchService</c>'s
/// relaxation chain re-querying the same title-only/keywords-only variants across requests) and
/// to buffer Open Library rate limits.
///
/// Both empty and non-empty responses are cached with the same TTL: a successful response with
/// zero docs is a legitimate result (Open Library processed the query and found nothing), not a
/// degraded one. Transient failures never reach this layer as a cacheable value - they surface as
/// exceptions from the inner client, handled by the HTTP resilience (retry/circuit-breaker)
/// pipeline before this decorator ever sees them.
/// </summary>
public sealed class CachingOpenLibraryClient(
    IOpenLibraryClient innerClient,
    IMemoryCache cache,
    IOptions<OpenLibraryCacheOptions> options) : IOpenLibraryClient
{
    public async Task<IReadOnlyList<OpenLibraryDoc>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var cacheKey = $"openlibrary:search:{query.Trim().ToLowerInvariant()}:{limit}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<OpenLibraryDoc>? cached) && cached is not null)
        {
            return cached;
        }

        var result = await innerClient.SearchAsync(query, limit, cancellationToken);

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(options.Value.CacheDurationMinutes));

        return result;
    }
}
