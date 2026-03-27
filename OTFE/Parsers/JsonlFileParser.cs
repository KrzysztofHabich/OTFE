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
        // Use FileShare.ReadWrite to allow other processes to write while we read
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
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

            if (root.TryGetProperty("type", out var typeProp) &&
                typeProp.ValueKind == JsonValueKind.String &&
                !string.Equals(typeProp.GetString(), "span", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping line {LineNumber} in {FilePath}: unsupported JSONL record type {RecordType}",
                    lineNumber, Path.GetFileName(filePath), typeProp.GetString());
                return null;
            }

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

                if (TimeSpan.TryParse(str, out var parsedDuration))
                {
                    return parsedDuration;
                }
            }
        }

        // Try start/end timestamps
        if (root.TryGetProperty("startTime", out var startProp) &&
            root.TryGetProperty("endTime", out var endProp))
        {
            if (TryParseTimestampValue(startProp, out var start) &&
                TryParseTimestampValue(endProp, out var end))
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
                if (TryParseTimestampValue(prop, out var timestamp))
                {
                    return timestamp.UtcDateTime;
                }
            }
        }

        return DateTime.MinValue;
    }

    private static IReadOnlyDictionary<string, string> ParseTags(JsonElement root)
    {
        var tags = new Dictionary<string, string>();

        var tagPropertyNames = new[] { "tags", "attributes" };

        foreach (var propName in tagPropertyNames)
        {
            if (root.TryGetProperty(propName, out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Object)
            {
                AddObjectTags(tags, tagsProp);
            }
        }

        if (root.TryGetProperty("resource", out var resourceProp) && resourceProp.ValueKind == JsonValueKind.Object)
        {
            if (resourceProp.TryGetProperty("attributes", out var resourceAttributes) && resourceAttributes.ValueKind == JsonValueKind.Object)
            {
                AddObjectTags(tags, resourceAttributes, overwriteExisting: false);
            }

            if (resourceProp.TryGetProperty("schemaUrl", out var resourceSchemaUrl) && resourceSchemaUrl.ValueKind == JsonValueKind.String)
            {
                tags.TryAdd("otel.resource.schema_url", resourceSchemaUrl.GetString() ?? string.Empty);
            }
        }

        if (root.TryGetProperty("instrumentationScope", out var scopeProp) && scopeProp.ValueKind == JsonValueKind.Object)
        {
            if (scopeProp.TryGetProperty("name", out var scopeName) && scopeName.ValueKind == JsonValueKind.String)
            {
                tags["otel.scope.name"] = scopeName.GetString() ?? string.Empty;
            }

            if (scopeProp.TryGetProperty("version", out var scopeVersion) && scopeVersion.ValueKind == JsonValueKind.String)
            {
                tags["otel.scope.version"] = scopeVersion.GetString() ?? string.Empty;
            }

            if (scopeProp.TryGetProperty("schemaUrl", out var scopeSchemaUrl) && scopeSchemaUrl.ValueKind == JsonValueKind.String)
            {
                tags["otel.scope.schema_url"] = scopeSchemaUrl.GetString() ?? string.Empty;
            }
        }

        if (root.TryGetProperty("kind", out var kindProp))
        {
            tags["otel.kind"] = kindProp.ValueKind == JsonValueKind.String
                ? kindProp.GetString() ?? string.Empty
                : kindProp.GetRawText();
        }

        if (root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            tags["otel.record.type"] = typeProp.GetString() ?? string.Empty;
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

            foreach (var timestampName in new[] { "timestamp", "time", "timeUnixNano" })
            {
                if (eventElement.TryGetProperty(timestampName, out var tsProp) &&
                    TryParseTimestampValue(tsProp, out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp.UtcDateTime;
                    break;
                }
            }

            var attributes = new Dictionary<string, string>();
            if (eventElement.TryGetProperty("attributes", out var attrProp) && attrProp.ValueKind == JsonValueKind.Object)
            {
                AddObjectTags(attributes, attrProp);
            }

            events.Add(new SpanEvent(timestamp, name, attributes));
        }

        return events;
    }

    private static void AddObjectTags(
        IDictionary<string, string> destination,
        JsonElement source,
        bool overwriteExisting = true)
    {
        foreach (var tag in source.EnumerateObject())
        {
            var value = tag.Value.ValueKind switch
            {
                JsonValueKind.String => tag.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => tag.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => tag.Value.GetRawText()
            };

            if (overwriteExisting)
            {
                destination[tag.Name] = value;
            }
            else
            {
                destination.TryAdd(tag.Name, value);
            }
        }
    }

    private static bool TryParseTimestampValue(JsonElement element, out DateTimeOffset timestamp)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return DateTimeOffset.TryParse(element.GetString(), out timestamp);

            case JsonValueKind.Number:
                return TryParseUnixTimestamp(element.GetInt64(), out timestamp);

            case JsonValueKind.Array:
                return TryParseTimestampTuple(element, out timestamp);

            default:
                timestamp = default;
                return false;
        }
    }

    private static bool TryParseUnixTimestamp(long value, out DateTimeOffset timestamp)
    {
        if (value > 1_000_000_000_000_000)
        {
            var seconds = value / 1_000_000_000;
            var nanoseconds = value % 1_000_000_000;
            timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);
            return true;
        }

        if (value > 1_000_000_000_000)
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(value);
            return true;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(value);
        return true;
    }

    private static bool TryParseTimestampTuple(JsonElement element, out DateTimeOffset timestamp)
    {
        timestamp = default;

        var enumerator = element.EnumerateArray();
        if (!enumerator.MoveNext() || enumerator.Current.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var seconds = enumerator.Current.GetInt64();
        if (!enumerator.MoveNext() || enumerator.Current.ValueKind != JsonValueKind.Number)
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        var nanoseconds = enumerator.Current.GetInt64();
        timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);
        return true;
    }
}
