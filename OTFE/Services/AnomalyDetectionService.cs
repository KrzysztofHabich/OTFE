using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using OTFE.Models;

namespace OTFE.Services;

/// <summary>
/// ML.NET-based anomaly detection service for trace data.
/// </summary>
public class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly ILogger<AnomalyDetectionService> _logger;
    private readonly MLContext _mlContext;
    private readonly HashSet<string> _anomalousTraceIds = [];

    public AnomalyDetectionService(ILogger<AnomalyDetectionService> logger)
    {
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
    }

    public async Task<IReadOnlyList<AnomalyResult>> DetectAnomaliesAsync(
        IReadOnlyList<Trace> traces,
        AnomalyDetectionConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new AnomalyDetectionConfig();
        var results = new List<AnomalyResult>();

        if (traces.Count < config.MinSamplesRequired)
        {
            _logger.LogWarning("Not enough traces for anomaly detection. Need {Required}, have {Count}",
                config.MinSamplesRequired, traces.Count);
            return results;
        }

        await Task.Run(() =>
        {
            // Group spans by name to detect anomalies within similar operations
            var allSpans = traces.SelectMany(t => t.AllSpans).ToList();
            var spansByName = allSpans
                .GroupBy(s => s.Name)
                .Where(g => g.Count() >= config.MinSamplesRequired)
                .ToList();

            foreach (var group in spansByName)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var spanAnomalies = DetectDurationAnomalies(group.ToList(), config);
                results.AddRange(spanAnomalies);
            }

            // Also detect overall trace duration anomalies
            var traceAnomalies = DetectTraceDurationAnomalies(traces, config);
            results.AddRange(traceAnomalies);

        }, cancellationToken);

        // Track anomalous trace IDs
        foreach (var result in results)
        {
            _anomalousTraceIds.Add(result.TraceId);
        }

        _logger.LogInformation("Detected {Count} anomalies across {TraceCount} traces",
            results.Count, _anomalousTraceIds.Count);

        return results;
    }

    public async Task<IReadOnlyList<AnomalyResult>> DetectSpanAnomaliesAsync(
        IReadOnlyList<Span> spans,
        AnomalyDetectionConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new AnomalyDetectionConfig();

        if (spans.Count < config.MinSamplesRequired)
        {
            return [];
        }

        return await Task.Run(() => DetectDurationAnomalies(spans.ToList(), config), cancellationToken);
    }

    public IReadOnlyList<string> GetAnomalousTraceIds()
    {
        return _anomalousTraceIds.ToList();
    }

    private List<AnomalyResult> DetectDurationAnomalies(List<Span> spans, AnomalyDetectionConfig config)
    {
        var results = new List<AnomalyResult>();

        try
        {
            // Use statistical approach: calculate mean and standard deviation
            var durations = spans.Select(s => s.Duration.TotalMilliseconds).ToArray();
            var mean = durations.Average();
            var stdDev = CalculateStdDev(durations, mean);

            // Use Z-score to detect anomalies
            var zThreshold = GetZScoreThreshold(config.ConfidenceLevel);

            foreach (var span in spans)
            {
                var duration = span.Duration.TotalMilliseconds;
                var zScore = stdDev > 0 ? Math.Abs((duration - mean) / stdDev) : 0;

                if (zScore > zThreshold || duration > mean * config.DurationThresholdMultiplier)
                {
                    var severity = Math.Min(1.0, zScore / (zThreshold * 2));

                    results.Add(new AnomalyResult
                    {
                        TraceId = span.TraceId,
                        SpanId = span.SpanId,
                        AnomalyType = "Duration",
                        Description = $"Span '{span.Name}' has unusual duration ({duration:F0}ms vs avg {mean:F0}ms)",
                        Severity = severity,
                        ExpectedValue = mean,
                        ActualValue = duration
                    });
                }
            }

            // Also use ML.NET IID Spike Detection for additional anomaly detection
            if (spans.Count >= 12) // Minimum required for spike detection
            {
                var mlAnomalies = DetectSpikesWithML(spans, config);
                results.AddRange(mlAnomalies);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting duration anomalies for {SpanName}", spans.FirstOrDefault()?.Name);
        }

        // Deduplicate results
        return results
            .GroupBy(r => (r.TraceId, r.SpanId))
            .Select(g => g.OrderByDescending(r => r.Severity).First())
            .ToList();
    }

    private List<AnomalyResult> DetectTraceDurationAnomalies(IReadOnlyList<Trace> traces, AnomalyDetectionConfig config)
    {
        var results = new List<AnomalyResult>();

        try
        {
            var durations = traces.Select(t => t.TotalDuration.TotalMilliseconds).ToArray();
            var mean = durations.Average();
            var stdDev = CalculateStdDev(durations, mean);
            var zThreshold = GetZScoreThreshold(config.ConfidenceLevel);

            foreach (var trace in traces)
            {
                var duration = trace.TotalDuration.TotalMilliseconds;
                var zScore = stdDev > 0 ? Math.Abs((duration - mean) / stdDev) : 0;

                if (zScore > zThreshold)
                {
                    var severity = Math.Min(1.0, zScore / (zThreshold * 2));

                    results.Add(new AnomalyResult
                    {
                        TraceId = trace.TraceId,
                        SpanId = trace.RootSpan?.SpanId ?? "unknown",
                        AnomalyType = "TraceDuration",
                        Description = $"Trace has unusual total duration ({duration:F0}ms vs avg {mean:F0}ms)",
                        Severity = severity,
                        ExpectedValue = mean,
                        ActualValue = duration
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting trace duration anomalies");
        }

        return results;
    }

    private List<AnomalyResult> DetectSpikesWithML(List<Span> spans, AnomalyDetectionConfig config)
    {
        var results = new List<AnomalyResult>();

        try
        {
            // Prepare data for ML.NET
            var dataPoints = spans
                .Select(s => new DurationDataPoint { Duration = (float)s.Duration.TotalMilliseconds })
                .ToList();

            var dataView = _mlContext.Data.LoadFromEnumerable(dataPoints);

            // Configure IID Spike Detection
            var iidSpikeEstimator = _mlContext.Transforms.DetectIidSpike(
                outputColumnName: nameof(DurationPrediction.Prediction),
                inputColumnName: nameof(DurationDataPoint.Duration),
                confidence: config.ConfidenceLevel,
                pvalueHistoryLength: Math.Min(spans.Count / 2, 50));

            var transformedData = iidSpikeEstimator.Fit(dataView).Transform(dataView);
            var predictions = _mlContext.Data.CreateEnumerable<DurationPrediction>(transformedData, reuseRowObject: false).ToList();

            for (int i = 0; i < predictions.Count && i < spans.Count; i++)
            {
                var prediction = predictions[i];
                // Prediction[0] = alert (1 = spike), Prediction[1] = score, Prediction[2] = p-value
                // Only flag as spike if p-value is below threshold (more statistically significant)
                if (prediction.Prediction is [1, var score, var pValue, ..] && pValue < (1 - config.ConfidenceLevel))
                {
                    var span = spans[i];
                    results.Add(new AnomalyResult
                    {
                        TraceId = span.TraceId,
                        SpanId = span.SpanId,
                        AnomalyType = "Spike",
                        Description = $"ML detected spike in '{span.Name}' (p-value: {pValue:F4})",
                        Severity = Math.Min(1.0, score / 10.0),
                        ActualValue = span.Duration.TotalMilliseconds
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML.NET spike detection failed, falling back to statistical methods");
        }

        return results;
    }

    private static double CalculateStdDev(double[] values, double mean)
    {
        if (values.Length <= 1) return 0;

        var sumSquaredDiffs = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSquaredDiffs / (values.Length - 1));
    }

    private static double GetZScoreThreshold(double confidenceLevel)
    {
        // Common Z-score thresholds for confidence levels
        return confidenceLevel switch
        {
            >= 0.99 => 2.576,
            >= 0.95 => 1.96,
            >= 0.90 => 1.645,
            _ => 1.645
        };
    }

    private class DurationDataPoint
    {
        public float Duration { get; set; }
    }

    private class DurationPrediction
    {
        [VectorType(3)]
        public double[]? Prediction { get; set; }
    }
}
