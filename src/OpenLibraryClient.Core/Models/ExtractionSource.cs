namespace OpenLibraryClient.Core.Models;

/// <summary>
/// Identifies which strategy produced an <see cref="ExtractionResult"/>, so downstream
/// consumers (e.g. the ranker, or the API response) can reason about how much to trust it.
/// </summary>
public enum ExtractionSource
{
    Deterministic,
    Llm
}
