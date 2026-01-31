using OTFE.Models;

namespace OTFE.Services;

/// <summary>
/// Represents an anomaly detected in trace data.
/// </summary>
public record AnomalyResult
{
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public required string AnomalyType { get; init; }
    public required string Description { get; init; }
    public required double Severity { get; init; } // 0-1 scale
    public double? ExpectedValue { get; init; }
    public double? ActualValue { get; init; }
}

/// <summary>
/// Configuration for anomaly detection.
/// </summary>
public record AnomalyDetectionConfig
{
    public double ConfidenceLevel { get; init; } = 0.95;
    public int MinSamplesRequired { get; init; } = 10;
    public double DurationThresholdMultiplier { get; init; } = 2.0; // Flag if > 2x average
}

/// <summary>
/// Service for detecting anomalies in trace data using ML.NET.
/// </summary>
public interface IAnomalyDetectionService
{
    /// <summary>
    /// Analyzes traces to detect duration anomalies.
    /// </summary>
    Task<IReadOnlyList<AnomalyResult>> DetectAnomaliesAsync(
        IReadOnlyList<Trace> traces,
        AnomalyDetectionConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects anomalies specifically within spans of the same name/type.
    /// </summary>
    Task<IReadOnlyList<AnomalyResult>> DetectSpanAnomaliesAsync(
        IReadOnlyList<Span> spans,
        AnomalyDetectionConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets traces that have been flagged as anomalous.
    /// </summary>
    IReadOnlyList<string> GetAnomalousTraceIds();
}
