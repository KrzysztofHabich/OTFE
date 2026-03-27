using System.IO;
using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Models;
using OTFE.Parsers;

namespace OTFE.Tests.Parsers;

public class JsonlFileParserTests
{
    private readonly JsonlFileParser _parser;

    public JsonlFileParserTests()
    {
        var logger = new Mock<ILogger<JsonlFileParser>>();
        _parser = new JsonlFileParser(logger.Object);
    }

    [Fact]
    public void SupportedExtensions_ContainsJsonl()
    {
        Assert.Contains(".jsonl", _parser.SupportedExtensions);
    }

    [Fact]
    public async Task ParseAsync_WithCopilotTraceJsonl_ParsesSpanRecord()
    {
        var tempFile = Path.GetTempFileName() + ".jsonl";

        try
        {
            var content = string.Join(Environment.NewLine,
                """
                {"type":"metric","name":"github.copilot.tool.call.count","value":1}
                """,
                """
                {"type":"span","traceId":"6318729bb484f4211c8495b95b4f84ce","spanId":"c1c6e58c9a49df8d","parentSpanId":"57a40e22f4850996","name":"execute_tool report_intent","kind":0,"startTime":[1774571412,243000000],"endTime":[1774571412,245918500],"attributes":{"gen_ai.tool.name":"report_intent","github.copilot.permission.result":"approved"},"status":{"code":0},"events":[{"name":"permission.checked","timestamp":[1774571412,244000000],"attributes":{"result":"approved"}}],"resource":{"attributes":{"service.name":"github-copilot","service.version":"1.0.10"}},"instrumentationScope":{"name":"langchainlab","version":"1.0.10"}}
                """);

            await File.WriteAllTextAsync(tempFile, content);

            var spans = await _parser.ParseAsync(tempFile);

            Assert.Single(spans);

            var span = spans[0];
            var expectedTimestamp = DateTimeOffset.FromUnixTimeSeconds(1774571412).AddTicks(243000000 / 100).UtcDateTime;

            Assert.Equal("6318729bb484f4211c8495b95b4f84ce", span.TraceId);
            Assert.Equal("c1c6e58c9a49df8d", span.SpanId);
            Assert.Equal("57a40e22f4850996", span.ParentId);
            Assert.Equal("execute_tool report_intent", span.Name);
            Assert.Equal(TimeSpan.FromTicks(29185), span.Duration);
            Assert.Equal(expectedTimestamp, span.Timestamp);
            Assert.Equal(SpanStatus.Unset, span.Status);
            Assert.Equal("report_intent", span.Tags["gen_ai.tool.name"]);
            Assert.Equal("github-copilot", span.Tags["service.name"]);
            Assert.Equal("langchainlab", span.Tags["otel.scope.name"]);
            Assert.Equal("1.0.10", span.Tags["otel.scope.version"]);
            Assert.Equal("0", span.Tags["otel.kind"]);
            Assert.Single(span.Events);
            Assert.Equal("permission.checked", span.Events[0].Name);
            Assert.Equal("approved", span.Events[0].Attributes["result"]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
