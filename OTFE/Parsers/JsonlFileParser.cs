using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OTFE.Models;

namespace OTFE.Parsers;

/// <summary>
/// Parses .jsonl files (JSON Lines format) containing OpenTelemetry spans.
/// </summary>
public class JsonlFileParser : ITraceParser
{
    private readonly ILogger<JsonlFileParser> _logger;

    public JsonlFileParser(ILogger<JsonlFileParser> logger)
    {
        _logger = logger;
    }

    public IEnumerable<string> SupportedExtensions => [".jsonl"];

    public async Task<IReadOnlyList<Span>> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var spans = new List<Span>();
        var lineNumber = 0;

        await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var span = ParseJsonLine(line, lineNumber, filePath);
            if (span != null)
            {
                spans.Add(span);
            }
        }

        return spans;
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    private Span? ParseJsonLine(string line, int lineNumber, string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var traceId = GetStringProperty(root, "traceId", "trace_id");
            var spanId = GetStringProperty(root, "spanId", "span_id");
            var parentId = GetStringProperty(root, "parentId", "parent_id", "parentSpanId") ?? "0000000000000000";
            var name = GetStringProperty(root, "name", "operationName");

            if (string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(spanId) || string.IsNullOrEmpty(name))
            {
                _logger.LogDebug("Skipping line {LineNumber} in {FilePath}: missing required fields (traceId, spanId, or name)", 
                    lineNumber, Path.GetFileName(filePath));
                return null;
            }

            var duration = ParseDuration(root);
            var status = ParseStatus(root);
            var timestamp = ParseTimestamp(root);
            var tags = ParseTags(root);
            var events = ParseEvents(root);

            return new Span
            {
                TraceId = traceId,
                SpanId = spanId,
                ParentId = parentId,
                Name = name,
                Duration = duration,
                Status = status,
                Timestamp = timestamp,
                Tags = tags,
                Events = events
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSON at line {LineNumber} in {FilePath}: {Message}", 
                lineNumber, Path.GetFileName(filePath), ex.Message);
            return null;
        }
    }

    private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static TimeSpan ParseDuration(JsonElement root)
    {
        // Try various duration formats
        if (root.TryGetProperty("duration", out var durationProp))
        {
            if (durationProp.ValueKind == JsonValueKind.Number)
            {
                // Assume nanoseconds (OpenTelemetry default)
                return TimeSpan.FromTicks(durationProp.GetInt64() / 100);
            }
            if (durationProp.ValueKind == JsonValueKind.String)
            {
                var str = durationProp.GetString();
                if (str != null && str.EndsWith("ms") && double.TryParse(str[..^2], out var ms))
                {
                    return TimeSpan.FromMilliseconds(ms);
                }
            }
        }

        // Try start/end timestamps
        if (root.TryGetProperty("startTime", out var startProp) &&
            root.TryGetProperty("endTime", out var endProp))
        {
            if (DateTime.TryParse(startProp.GetString(), out var start) &&
                DateTime.TryParse(endProp.GetString(), out var end))
            {
                return end - start;
            }
        }

        return TimeSpan.Zero;
    }

    private static SpanStatus ParseStatus(JsonElement root)
    {
        if (root.TryGetProperty("status", out var statusProp))
        {
            if (statusProp.ValueKind == JsonValueKind.Object)
            {
                if (statusProp.TryGetProperty("code", out var codeProp))
                {
                    var code = codeProp.ValueKind == JsonValueKind.Number
                        ? codeProp.GetInt32()
                        : codeProp.GetString()?.ToLowerInvariant() switch
                        {
                            "ok" => 1,
                            "error" => 2,
                            _ => 0
                        };

                    return code switch
                    {
                        1 => SpanStatus.Ok,
                        2 => SpanStatus.Error,
                        _ => SpanStatus.Unset
                    };
                }
            }
            else if (statusProp.ValueKind == JsonValueKind.String)
            {
                return statusProp.GetString()?.ToLowerInvariant() switch
                {
                    "ok" => SpanStatus.Ok,
                    "error" => SpanStatus.Error,
                    _ => SpanStatus.Unset
                };
            }
        }

        return SpanStatus.Unset;
    }

    private static DateTime ParseTimestamp(JsonElement root)
    {
        var timestampNames = new[] { "timestamp", "startTime", "start_time", "time" };

        foreach (var name in timestampNames)
        {
            if (root.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String && DateTime.TryParse(prop.GetString(), out var dt))
                {
                    return dt;
                }
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    // Unix timestamp in milliseconds or nanoseconds
                    var value = prop.GetInt64();
                    if (value > 1_000_000_000_000_000) // Nanoseconds
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000_000).DateTime;
                    }
                    if (value > 1_000_000_000_000) // Milliseconds
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(value).DateTime;
                    }
                    // Seconds
                    return DateTimeOffset.FromUnixTimeSeconds(value).DateTime;
                }
            }
        }

        return DateTime.MinValue;
    }

    private static IReadOnlyDictionary<string, string> ParseTags(JsonElement root)
    {
        var tags = new Dictionary<string, string>();

        var tagPropertyNames = new[] { "tags", "attributes", "resource" };

        foreach (var propName in tagPropertyNames)
        {
            if (root.TryGetProperty(propName, out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var tag in tagsProp.EnumerateObject())
                {
                    var value = tag.Value.ValueKind switch
                    {
                        JsonValueKind.String => tag.Value.GetString() ?? "",
                        JsonValueKind.Number => tag.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => tag.Value.GetRawText()
                    };
                    tags[tag.Name] = value;
                }
            }
        }

        return tags;
    }

    private static IReadOnlyList<SpanEvent> ParseEvents(JsonElement root)
    {
        var events = new List<SpanEvent>();

        if (!root.TryGetProperty("events", out var eventsProp) || eventsProp.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var eventElement in eventsProp.EnumerateArray())
        {
            var name = GetStringProperty(eventElement, "name", "message") ?? "event";
            var timestamp = DateTime.MinValue;

            if (eventElement.TryGetProperty("timestamp", out var tsProp))
            {
                if (tsProp.ValueKind == JsonValueKind.String)
                {
                    DateTime.TryParse(tsProp.GetString(), out timestamp);
                }
            }

            var attributes = new Dictionary<string, string>();
            if (eventElement.TryGetProperty("attributes", out var attrProp) && attrProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var attr in attrProp.EnumerateObject())
                {
                    attributes[attr.Name] = attr.Value.ValueKind == JsonValueKind.String
                        ? attr.Value.GetString() ?? ""
                        : attr.Value.GetRawText();
                }
            }

            events.Add(new SpanEvent(timestamp, name, attributes));
        }

        return events;
    }
}
