namespace OpenLibraryClient.Infrastructure.Extraction;

/// <summary>
/// The exact JSON shape requested from the LLM via structured output. Keep this narrow and
/// flat - it's serialized straight into a JSON schema sent to the model.
/// </summary>
internal sealed class LlmExtractionDto
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public List<string> Keywords { get; set; } = [];

    /// <summary>A short, one-to-two sentence rationale for why the model extracted these fields.</summary>
    public string? Explanation { get; set; }
}
