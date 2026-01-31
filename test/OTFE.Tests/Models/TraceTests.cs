using OTFE.Models;

namespace OTFE.Tests.Models;

public class TraceTests
{
    [Fact]
    public void TotalDuration_ReturnsRootSpanDuration()
    {
        // Arrange
        var rootSpan = CreateSpan(duration: TimeSpan.FromMilliseconds(500), isRoot: true);
        var trace = CreateTrace(rootSpan, [rootSpan]);

        // Act & Assert
        Assert.Equal(TimeSpan.FromMilliseconds(500), trace.TotalDuration);
    }

    [Fact]
    public void Status_WhenAnySpanHasError_ReturnsError()
    {
        // Arrange
        var rootSpan = CreateSpan(status: SpanStatus.Ok, isRoot: true);
        var childSpan = CreateSpan(status: SpanStatus.Error, parentId: rootSpan.SpanId);
        var trace = CreateTrace(rootSpan, [rootSpan, childSpan]);

        // Act & Assert
        Assert.Equal(SpanStatus.Error, trace.Status);
    }

    [Fact]
    public void Status_WhenAllSpansAreOk_ReturnsOk()
    {
        // Arrange
        var rootSpan = CreateSpan(status: SpanStatus.Ok, isRoot: true);
        var childSpan = CreateSpan(status: SpanStatus.Ok, parentId: rootSpan.SpanId);
        var trace = CreateTrace(rootSpan, [rootSpan, childSpan]);

        // Act & Assert
        Assert.Equal(SpanStatus.Ok, trace.Status);
    }

    [Fact]
    public void Status_WhenMixedOkAndUnset_ReturnsUnset()
    {
        // Arrange
        var rootSpan = CreateSpan(status: SpanStatus.Ok, isRoot: true);
        var childSpan = CreateSpan(status: SpanStatus.Unset, parentId: rootSpan.SpanId);
        var trace = CreateTrace(rootSpan, [rootSpan, childSpan]);

        // Act & Assert
        Assert.Equal(SpanStatus.Unset, trace.Status);
    }

    [Fact]
    public void EntryPoint_ReturnsRootSpanName()
    {
        // Arrange
        var rootSpan = CreateSpan(name: "HTTP GET /api/users", isRoot: true);
        var trace = CreateTrace(rootSpan, [rootSpan]);

        // Act & Assert
        Assert.Equal("HTTP GET /api/users", trace.EntryPoint);
    }

    [Fact]
    public void SpanCount_ReturnsCorrectCount()
    {
        // Arrange
        var rootSpan = CreateSpan(isRoot: true);
        var child1 = CreateSpan(parentId: rootSpan.SpanId, spanId: "child1");
        var child2 = CreateSpan(parentId: rootSpan.SpanId, spanId: "child2");
        var trace = CreateTrace(rootSpan, [rootSpan, child1, child2]);

        // Act & Assert
        Assert.Equal(3, trace.SpanCount);
    }

    [Fact]
    public void GetChildren_ReturnsCorrectChildSpans()
    {
        // Arrange
        var rootSpan = CreateSpan(isRoot: true, spanId: "root");
        var child1 = CreateSpan(parentId: "root", spanId: "child1");
        var child2 = CreateSpan(parentId: "root", spanId: "child2");
        var grandChild = CreateSpan(parentId: "child1", spanId: "grandchild");
        var trace = CreateTrace(rootSpan, [rootSpan, child1, child2, grandChild]);

        // Act
        var rootChildren = trace.GetChildren(rootSpan).ToList();
        var child1Children = trace.GetChildren(child1).ToList();

        // Assert
        Assert.Equal(2, rootChildren.Count);
        Assert.Contains(rootChildren, s => s.SpanId == "child1");
        Assert.Contains(rootChildren, s => s.SpanId == "child2");
        Assert.Single(child1Children);
        Assert.Equal("grandchild", child1Children[0].SpanId);
    }

    private static Span CreateSpan(
        string spanId = "span123",
        string parentId = "parent456",
        string name = "TestSpan",
        TimeSpan? duration = null,
        SpanStatus status = SpanStatus.Ok,
        bool isRoot = false)
    {
        return new Span
        {
            TraceId = "trace123",
            SpanId = spanId,
            ParentId = isRoot ? "0000000000000000" : parentId,
            Name = name,
            Duration = duration ?? TimeSpan.FromMilliseconds(100),
            Status = status,
            Timestamp = DateTime.UtcNow
        };
    }

    private static Trace CreateTrace(Span rootSpan, IReadOnlyList<Span> allSpans)
    {
        return new Trace
        {
            TraceId = rootSpan.TraceId,
            RootSpan = rootSpan,
            AllSpans = allSpans
        };
    }
}
