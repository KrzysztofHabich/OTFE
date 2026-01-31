using OTFE.Models;

namespace OTFE.Tests.Models;

public class SpanTests
{
    [Fact]
    public void IsRoot_WhenParentIdIsZeros_ReturnsTrue()
    {
        // Arrange
        var span = CreateSpan(parentId: "0000000000000000");

        // Act & Assert
        Assert.True(span.IsRoot);
    }

    [Fact]
    public void IsRoot_WhenParentIdIsEmpty_ReturnsTrue()
    {
        // Arrange
        var span = CreateSpan(parentId: "");

        // Act & Assert
        Assert.True(span.IsRoot);
    }

    [Fact]
    public void IsRoot_WhenParentIdHasValue_ReturnsFalse()
    {
        // Arrange
        var span = CreateSpan(parentId: "abc123def456");

        // Act & Assert
        Assert.False(span.IsRoot);
    }

    [Fact]
    public void Span_DefaultTags_IsEmptyDictionary()
    {
        // Arrange & Act
        var span = CreateSpan();

        // Assert
        Assert.Empty(span.Tags);
    }

    [Fact]
    public void Span_DefaultEvents_IsEmptyList()
    {
        // Arrange & Act
        var span = CreateSpan();

        // Assert
        Assert.Empty(span.Events);
    }

    private static Span CreateSpan(
        string traceId = "trace123",
        string spanId = "span456",
        string parentId = "parent789",
        string name = "TestSpan",
        TimeSpan? duration = null,
        SpanStatus status = SpanStatus.Ok)
    {
        return new Span
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentId = parentId,
            Name = name,
            Duration = duration ?? TimeSpan.FromMilliseconds(100),
            Status = status,
            Timestamp = DateTime.UtcNow
        };
    }
}
