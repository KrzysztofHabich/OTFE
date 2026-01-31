namespace OTFE.Models;

/// <summary>
/// Represents a single OpenTelemetry span.
/// </summary>
public record Span
{
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public required string ParentId { get; init; }
    public required string Name { get; init; }
    public required TimeSpan Duration { get; init; }
    public required SpanStatus Status { get; init; }
    public required DateTime Timestamp { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SpanEvent> Events { get; init; } = [];

    /// <summary>
    /// Returns true if this span has no parent (root span).
    /// </summary>
    public bool IsRoot => ParentId == "0000000000000000" || string.IsNullOrEmpty(ParentId);
}
