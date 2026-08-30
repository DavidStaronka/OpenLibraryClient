namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>Binds to the "Gemini" configuration section (cache-related settings only).</summary>
public sealed class LlmCacheOptions
{
    /// <summary>
    /// How long an LLM extraction result is cached for, keyed by normalized raw query text. The
    /// same free-text input reliably produces the same (or an equivalent) extraction, and Gemini
    /// calls are the slowest/most expensive part of the search pipeline, so a long TTL is safe.
    /// </summary>
    public int CacheDurationHours { get; set; } = 24;
}
