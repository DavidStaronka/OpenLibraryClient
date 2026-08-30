namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>Binds to the "Gemini:Resilience" configuration section.</summary>
public sealed class GeminiResilienceOptions
{
    /// <summary>Number of retry attempts for transient failures (timeouts, 429, 5xx) before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Per-attempt timeout, in seconds, applied to each individual call (including retries).</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
