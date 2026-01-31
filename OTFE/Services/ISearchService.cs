using OTFE.Models;

namespace OTFE.Services;

/// <summary>
/// Interface for searching and filtering traces and spans.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Parses a search query string into a filter.
    /// </summary>
    TraceFilter ParseQuery(string query);

    /// <summary>
    /// Filters traces based on the given filter.
    /// </summary>
    IReadOnlyList<Trace> FilterTraces(IEnumerable<Trace> traces, TraceFilter filter);

    /// <summary>
    /// Sets the list of anomalous trace IDs for HasAnomalies filtering.
    /// </summary>
    void SetAnomalousTraceIds(IReadOnlyList<string> traceIds);
}

/// <summary>
/// Represents a parsed search filter.
/// </summary>
public record TraceFilter
{
    public SpanStatus? Status { get; init; }
    public double? MinDurationMs { get; init; }
    public double? MaxDurationMs { get; init; }
    public string? NameContains { get; init; }
    public Dictionary<string, string> TagFilters { get; init; } = [];
    public bool HasError { get; init; }
    public bool HasAnomalies { get; init; }
    public string? RawQuery { get; init; }

    public bool IsEmpty => Status == null && MinDurationMs == null && MaxDurationMs == null 
        && string.IsNullOrEmpty(NameContains) && TagFilters.Count == 0 && !HasError && !HasAnomalies;
}
