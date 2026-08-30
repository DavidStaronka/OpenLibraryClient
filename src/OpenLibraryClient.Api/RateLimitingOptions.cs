namespace OpenLibraryClient.Api;

/// <summary>
/// Binds to the "RateLimiting" configuration section. Governs the fixed-window limiter applied
/// to GET /api/books/search, partitioned per client IP - each search can trigger a billable
/// Gemini call, so this bounds the cost/abuse surface of an unauthenticated public endpoint.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Maximum number of requests a single client (by IP) may make within one window.</summary>
    public int PermitLimit { get; set; } = 20;

    /// <summary>Length, in seconds, of the fixed window that <see cref="PermitLimit"/> applies to.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// How many requests beyond <see cref="PermitLimit"/> are queued (processed once capacity
    /// frees up) rather than immediately rejected with 429. Zero means reject immediately.
    /// </summary>
    public int QueueLimit { get; set; }
}
