using System.Diagnostics.Metrics;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Infrastructure.Search;

/// <summary>
/// Domain-specific metrics for the book search pipeline, recorded via the standard
/// System.Diagnostics.Metrics API (no direct OpenTelemetry package dependency here - the host
/// just needs to subscribe to this Meter's name, see Program.cs's `AddMeter(MeterName)`).
///
/// These exist alongside the free ASP.NET Core/HttpClient instrumentation to answer
/// pipeline-specific questions the generic metrics can't: how often the LLM is actually invoked
/// (cost/latency implications), how often deterministic extraction fails validation against real
/// Open Library data, and how deep the query-relaxation retry chain typically goes.
/// </summary>
public sealed class BookSearchMetrics : IDisposable
{
    public const string MeterName = "OpenLibraryClient.BookSearch";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _extractionSource;
    private readonly Counter<long> _llmFallback;
    private readonly Histogram<long> _queriesAttempted;
    private readonly Histogram<long> _resultsCount;
    private readonly Counter<long> _llmExtractionOutcome;

    public BookSearchMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _requests = _meter.CreateCounter<long>(
            "book_search.requests",
            description: "Total number of book search requests, by outcome.");

        _duration = _meter.CreateHistogram<double>(
            "book_search.duration",
            unit: "s",
            description: "End-to-end SearchAsync duration, by the extraction source ultimately used.");

        _extractionSource = _meter.CreateCounter<long>(
            "book_search.extraction.source",
            description: "How often each extraction source (Deterministic/Llm) is the one ultimately used to query Open Library.");

        _llmFallback = _meter.CreateCounter<long>(
            "book_search.llm_fallback",
            description: "How often - and why - a deterministic extraction triggers an LLM second opinion.");

        _queriesAttempted = _meter.CreateHistogram<long>(
            "book_search.queries_attempted",
            description: "Number of distinct Open Library queries attempted per search (depth of the relaxation retry chain).");

        _resultsCount = _meter.CreateHistogram<long>(
            "book_search.results_count",
            description: "Number of ranked results ultimately returned per search (0 indicates a dead-end search).");

        _llmExtractionOutcome = _meter.CreateCounter<long>(
            "book_search.llm_extraction_outcome",
            description: "Outcome of each LLM extraction attempt: success, failure (transient error survived retries), " +
                "or not-configured (no Gemini API key set) - lets dashboards distinguish misconfiguration from a genuine outage.");
    }

    public void RecordSuccess(BookSearchResult result, TimeSpan elapsed)
    {
        _requests.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
        _duration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("extraction_source", result.Extraction.Source.ToString()));
        _extractionSource.Add(1, new KeyValuePair<string, object?>("source", result.Extraction.Source.ToString()));
        _queriesAttempted.Record(result.QueriesAttempted.Count);
        _resultsCount.Record(result.Results.Count);
    }

    public void RecordFailure(TimeSpan elapsed)
    {
        _requests.Add(1, new KeyValuePair<string, object?>("outcome", "error"));
        _duration.Record(elapsed.TotalSeconds);
    }

    /// <summary>Records that a deterministic extraction wasn't trusted as-is and the LLM was consulted for a second opinion.</summary>
    public void RecordLlmFallback(string reason) =>
        _llmFallback.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records the outcome of a single LLM extraction attempt: "success", "failure", or "not-configured".</summary>
    public void RecordLlmExtractionOutcome(string outcome) =>
        _llmExtractionOutcome.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
