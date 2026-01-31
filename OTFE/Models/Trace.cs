namespace OTFE.Models;

/// <summary>
/// Represents a complete trace composed of multiple spans.
/// </summary>
public class Trace
{
    public required string TraceId { get; init; }
    public required Span RootSpan { get; init; }
    public required IReadOnlyList<Span> AllSpans { get; init; }

    /// <summary>
    /// Total duration of the trace (from root span).
    /// </summary>
    public TimeSpan TotalDuration => RootSpan.Duration;

    /// <summary>
    /// Overall status of the trace. Error if any span has error status.
    /// </summary>
    public SpanStatus Status => AllSpans.Any(s => s.Status == SpanStatus.Error)
        ? SpanStatus.Error
        : AllSpans.All(s => s.Status == SpanStatus.Ok)
            ? SpanStatus.Ok
            : SpanStatus.Unset;

    /// <summary>
    /// Entry point name (root span name).
    /// </summary>
    public string EntryPoint => RootSpan.Name;

    /// <summary>
    /// Number of spans in this trace.
    /// </summary>
    public int SpanCount => AllSpans.Count;

    /// <summary>
    /// Gets child spans of the specified parent span.
    /// </summary>
    public IEnumerable<Span> GetChildren(Span parent)
    {
        return AllSpans.Where(s => s.ParentId == parent.SpanId);
    }
}
