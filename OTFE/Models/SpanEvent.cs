namespace OTFE.Models;

/// <summary>
/// Represents an event within a span (e.g., exception, log message).
/// </summary>
public record SpanEvent(
    DateTime Timestamp,
    string Name,
    IReadOnlyDictionary<string, string> Attributes
);
