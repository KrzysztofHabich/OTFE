using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Models;
using OTFE.Services;

namespace OTFE.Tests.Services;

public class AnomalyDetectionServiceTests
{
    private readonly AnomalyDetectionService _service;

    public AnomalyDetectionServiceTests()
    {
        var logger = new Mock<ILogger<AnomalyDetectionService>>();
        _service = new AnomalyDetectionService(logger.Object);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WithTooFewTraces_ReturnsEmpty()
    {
        // Arrange
        var traces = CreateTraces(5); // Less than minimum required

        // Act
        var results = await _service.DetectAnomaliesAsync(traces, new AnomalyDetectionConfig { MinSamplesRequired = 10 });

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WithUniformDurations_ReturnsEmpty()
    {
        // Arrange - all traces with same duration
        var traces = Enumerable.Range(0, 20)
            .Select(i => CreateTrace($"trace{i}", TimeSpan.FromMilliseconds(100)))
            .ToList();

        // Act
        var results = await _service.DetectAnomaliesAsync(traces);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WithOutlier_DetectsAnomaly()
    {
        // Arrange - mostly uniform with one outlier
        var traces = Enumerable.Range(0, 19)
            .Select(i => CreateTrace($"trace{i}", TimeSpan.FromMilliseconds(100)))
            .ToList();

        // Add significant outlier (10x normal duration)
        traces.Add(CreateTrace("outlier", TimeSpan.FromMilliseconds(1000)));

        // Act
        var results = await _service.DetectAnomaliesAsync(traces);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.TraceId == "outlier");
    }

    [Fact]
    public async Task DetectSpanAnomaliesAsync_WithTooFewSpans_ReturnsEmpty()
    {
        // Arrange
        var spans = new List<Span>
        {
            CreateSpan("span1", TimeSpan.FromMilliseconds(100)),
            CreateSpan("span2", TimeSpan.FromMilliseconds(100))
        };

        // Act
        var results = await _service.DetectSpanAnomaliesAsync(spans, new AnomalyDetectionConfig { MinSamplesRequired = 10 });

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task DetectSpanAnomaliesAsync_WithOutlier_DetectsAnomaly()
    {
        // Arrange
        var spans = Enumerable.Range(0, 19)
            .Select(i => CreateSpan($"span{i}", TimeSpan.FromMilliseconds(100)))
            .ToList();

        // Add outlier
        spans.Add(CreateSpan("outlier", TimeSpan.FromMilliseconds(1000)));

        // Act
        var results = await _service.DetectSpanAnomaliesAsync(spans);

        // Assert
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task GetAnomalousTraceIds_AfterDetection_ReturnsCorrectIds()
    {
        // Arrange
        var traces = Enumerable.Range(0, 19)
            .Select(i => CreateTrace($"trace{i}", TimeSpan.FromMilliseconds(100)))
            .ToList();
        traces.Add(CreateTrace("outlier", TimeSpan.FromMilliseconds(1000)));

        // Act
        await _service.DetectAnomaliesAsync(traces);
        var anomalousIds = _service.GetAnomalousTraceIds();

        // Assert
        Assert.Contains("outlier", anomalousIds);
    }

    [Fact]
    public async Task DetectAnomaliesAsync_WithCancellation_ThrowsOperationCanceled()
    {
        // Arrange
        var traces = CreateTraces(100);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - TaskCanceledException is a subtype of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.DetectAnomaliesAsync(traces, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DetectAnomaliesAsync_AnomalyResultHasCorrectProperties()
    {
        // Arrange
        var traces = Enumerable.Range(0, 19)
            .Select(i => CreateTrace($"trace{i}", TimeSpan.FromMilliseconds(100)))
            .ToList();
        traces.Add(CreateTrace("outlier", TimeSpan.FromMilliseconds(1000)));

        // Act
        var results = await _service.DetectAnomaliesAsync(traces);

        // Assert
        var outlierResult = results.FirstOrDefault(r => r.TraceId == "outlier");
        Assert.NotNull(outlierResult);
        Assert.NotEmpty(outlierResult.AnomalyType);
        Assert.NotEmpty(outlierResult.Description);
        Assert.InRange(outlierResult.Severity, 0, 1);
    }

    private static List<Trace> CreateTraces(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateTrace($"trace{i}", TimeSpan.FromMilliseconds(100 + (i % 3) * 10)))
            .ToList();
    }

    private static Trace CreateTrace(string traceId, TimeSpan duration)
    {
        var rootSpan = new Span
        {
            TraceId = traceId,
            SpanId = $"span-{traceId}",
            ParentId = "0000000000000000",
            Name = "TestOperation",
            Status = SpanStatus.Ok,
            Duration = duration,
            Timestamp = DateTime.UtcNow
        };

        return new Trace
        {
            TraceId = traceId,
            RootSpan = rootSpan,
            AllSpans = [rootSpan]
        };
    }

    private static Span CreateSpan(string spanId, TimeSpan duration)
    {
        return new Span
        {
            TraceId = "test-trace",
            SpanId = spanId,
            ParentId = "0000000000000000",
            Name = "TestOperation",
            Status = SpanStatus.Ok,
            Duration = duration,
            Timestamp = DateTime.UtcNow
        };
    }
}
