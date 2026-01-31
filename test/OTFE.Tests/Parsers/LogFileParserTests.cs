using System.IO;
using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Models;
using OTFE.Parsers;

namespace OTFE.Tests.Parsers;

public class LogFileParserTests
{
    private readonly LogFileParser _parser;

    public LogFileParserTests()
    {
        var logger = new Mock<ILogger<LogFileParser>>();
        _parser = new LogFileParser(logger.Object);
    }

    [Fact]
    public void SupportedExtensions_ContainsLog()
    {
        // Assert
        Assert.Contains(".log", _parser.SupportedExtensions);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ReturnsSpans()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            // Skip test if sample file doesn't exist
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        Assert.NotEmpty(spans);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesTraceId()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var firstSpan = spans.First();
        Assert.Equal("7b93d1d92e416e89ce4ab528ef924a71", firstSpan.TraceId);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesSpanId()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var firstSpan = spans.First();
        Assert.Equal("b568718bd3b5076b", firstSpan.SpanId);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesDuration()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var firstSpan = spans.First();
        Assert.Equal(TimeSpan.FromMilliseconds(177.2474), firstSpan.Duration);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesTags()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var firstSpan = spans.First();
        Assert.Contains("http.request.method", firstSpan.Tags.Keys);
        Assert.Equal("GET", firstSpan.Tags["http.request.method"]);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesErrorStatus()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var errorSpan = spans.FirstOrDefault(s => s.Status == SpanStatus.Error);
        Assert.NotNull(errorSpan);
    }

    [Fact]
    public async Task ParseAsync_WithSampleFile_ParsesEvents()
    {
        // Arrange
        var sampleFile = GetSampleFilePath("traces_20260130_201555.log");
        if (!File.Exists(sampleFile))
        {
            return;
        }

        // Act
        var spans = await _parser.ParseAsync(sampleFile);

        // Assert
        var spanWithEvents = spans.FirstOrDefault(s => s.Events.Count > 0);
        Assert.NotNull(spanWithEvents);
        Assert.Contains(spanWithEvents.Events, e => e.Name == "exception");
    }

    [Fact]
    public async Task ParseAsync_WithInlineContent_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".log";
        try
        {
            var content = """
                [2026-01-30 20:16:00.949] TRACE
                TraceId: abc123
                SpanId: def456
                ParentId: 0000000000000000
                Name: TestOperation
                Duration: 100.5ms
                Status: Ok
                Tags:
                  test.key = test.value
                --------------------------------------------------------------------------------
                """;

            await File.WriteAllTextAsync(tempFile, content);

            // Act
            var spans = await _parser.ParseAsync(tempFile);

            // Assert
            Assert.Single(spans);
            var span = spans[0];
            Assert.Equal("abc123", span.TraceId);
            Assert.Equal("def456", span.SpanId);
            Assert.True(span.IsRoot);
            Assert.Equal("TestOperation", span.Name);
            Assert.Equal(TimeSpan.FromMilliseconds(100.5), span.Duration);
            Assert.Equal(SpanStatus.Ok, span.Status);
            Assert.Equal("test.value", span.Tags["test.key"]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static string GetSampleFilePath(string fileName)
    {
        // Navigate from test output to solution root
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", ".."));
        return Path.Combine(solutionDir, "Samples", fileName);
    }
}
