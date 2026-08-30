namespace OpenLibraryClient.Infrastructure.OpenLibrary;

/// <summary>Binds to the "OpenLibrary" configuration section.</summary>
public sealed class OpenLibraryCacheOptions
{
    /// <summary>
    /// How long a search result (empty or non-empty) is cached for, keyed by normalized query
    /// text + limit. Open Library metadata (ratings, want-to-read counts, etc.) changes slowly,
    /// so a relatively long TTL is safe.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 20;
}
