using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Parsers;

namespace OTFE.Tests.Parsers;

public class TraceParserFactoryTests
{
    private readonly TraceParserFactory _factory;

    public TraceParserFactoryTests()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _factory = new TraceParserFactory(loggerFactory.Object);
    }

    [Theory]
    [InlineData("test.log", typeof(LogFileParser))]
    [InlineData("test.LOG", typeof(LogFileParser))]
    [InlineData("test.jsonl", typeof(JsonlFileParser))]
    [InlineData("test.JSONL", typeof(JsonlFileParser))]
    public void GetParser_WithSupportedExtension_ReturnsCorrectParser(string filePath, Type expectedType)
    {
        // Act
        var parser = _factory.GetParser(filePath);

        // Assert
        Assert.NotNull(parser);
        Assert.IsType(expectedType, parser);
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.json")]
    [InlineData("test.xml")]
    public void GetParser_WithUnsupportedExtension_ReturnsNull(string filePath)
    {
        // Act
        var parser = _factory.GetParser(filePath);

        // Assert
        Assert.Null(parser);
    }

    [Fact]
    public void SupportedExtensions_ContainsExpectedExtensions()
    {
        // Act
        var extensions = _factory.SupportedExtensions.ToList();

        // Assert
        Assert.Contains(".log", extensions);
        Assert.Contains(".jsonl", extensions);
    }
}
