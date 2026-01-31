using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OTFE.Models;

namespace OTFE.Parsers;

/// <summary>
/// Parses .log files in the custom C# Logger format.
/// </summary>
public partial class LogFileParser : ITraceParser
{
    private readonly ILogger<LogFileParser> _logger;

    public LogFileParser(ILogger<LogFileParser> logger)
    {
        _logger = logger;
    }

    public IEnumerable<string> SupportedExtensions => [".log"];

    public async Task<IReadOnlyList<Span>> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var spans = new List<Span>();
        var lines = new List<string>();
        var blockNumber = 0;
        var fileName = Path.GetFileName(filePath);

        await foreach (var line in ReadLinesAsync(filePath, cancellationToken))
        {
            if (line.StartsWith("---"))
            {
                // Separator line - process accumulated lines as a span block
                if (lines.Count > 0)
                {
                    blockNumber++;
                    var span = ParseSpanBlock(lines, blockNumber, fileName);
                    if (span != null)
                    {
                        spans.Add(span);
                    }
                    lines.Clear();
                }
            }
            else
            {
                lines.Add(line);
            }
        }

        // Process any remaining lines
        if (lines.Count > 0)
        {
            blockNumber++;
            var span = ParseSpanBlock(lines, blockNumber, fileName);
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

    private Span? ParseSpanBlock(List<string> lines, int blockNumber, string fileName)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        string? traceId = null;
        string? spanId = null;
        string? parentId = null;
        string? name = null;
        TimeSpan duration = TimeSpan.Zero;
        SpanStatus status = SpanStatus.Unset;
        DateTime timestamp = DateTime.MinValue;
        var tags = new Dictionary<string, string>();
        var events = new List<SpanEvent>();

        var currentSection = Section.None;
        SpanEvent? currentEvent = null;
        var currentEventAttributes = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            // Parse header line: [2026-01-30 20:16:00.949] TRACE
            if (line.StartsWith('[') && line.Contains("] TRACE"))
            {
                var match = TimestampRegex().Match(line);
                if (match.Success && DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                {
                    timestamp = ts;
                }
                continue;
            }

            // Parse key-value fields
            if (line.StartsWith("TraceId:"))
            {
                traceId = line["TraceId:".Length..].Trim();
                currentSection = Section.None;
            }
            else if (line.StartsWith("SpanId:"))
            {
                spanId = line["SpanId:".Length..].Trim();
                currentSection = Section.None;
            }
            else if (line.StartsWith("ParentId:"))
            {
                parentId = line["ParentId:".Length..].Trim();
                currentSection = Section.None;
            }
            else if (line.StartsWith("Name:"))
            {
                name = line["Name:".Length..].Trim();
                currentSection = Section.None;
            }
            else if (line.StartsWith("Duration:"))
            {
                duration = ParseDuration(line["Duration:".Length..].Trim());
                currentSection = Section.None;
            }
            else if (line.StartsWith("Status:"))
            {
                status = ParseStatus(line["Status:".Length..].Trim());
                currentSection = Section.None;
            }
            else if (line.StartsWith("Tags:"))
            {
                currentSection = Section.Tags;
            }
            else if (line.StartsWith("Events:"))
            {
                // Save any pending event
                if (currentEvent != null)
                {
                    events.Add(currentEvent with { Attributes = currentEventAttributes });
                    currentEventAttributes = [];
                }
                currentSection = Section.Events;
            }
            else if (currentSection == Section.Tags && line.StartsWith("  "))
            {
                // Parse tag: "  key = value"
                var tagLine = line.TrimStart();
                var eqIndex = tagLine.IndexOf(" = ", StringComparison.Ordinal);
                if (eqIndex > 0)
                {
                    var key = tagLine[..eqIndex];
                    var value = tagLine[(eqIndex + 3)..];
                    tags[key] = value;
                }
            }
            else if (currentSection == Section.Events)
            {
                // Event header: "  [20:16:04.786] exception"
                var eventMatch = EventHeaderRegex().Match(line);
                if (eventMatch.Success)
                {
                    // Save previous event
                    if (currentEvent != null)
                    {
                        events.Add(currentEvent with { Attributes = currentEventAttributes });
                        currentEventAttributes = [];
                    }

                    var eventTime = eventMatch.Groups[1].Value;
                    var eventName = eventMatch.Groups[2].Value;

                    // Combine date from timestamp with time from event
                    var eventTimestamp = timestamp.Date;
                    if (TimeSpan.TryParse(eventTime, out var timeOfDay))
                    {
                        eventTimestamp = eventTimestamp.Add(timeOfDay);
                    }

                    currentEvent = new SpanEvent(eventTimestamp, eventName, new Dictionary<string, string>());
                }
                else if (line.StartsWith("    ") && currentEvent != null)
                {
                    // Event attribute: "    exception.message = ..."
                    var attrLine = line.TrimStart();
                    var eqIndex = attrLine.IndexOf(" = ", StringComparison.Ordinal);
                    if (eqIndex > 0)
                    {
                        var key = attrLine[..eqIndex];
                        var value = attrLine[(eqIndex + 3)..];
                        currentEventAttributes[key] = value;
                    }
                    else
                    {
                        // Continuation of previous attribute value (e.g., stack trace)
                        if (currentEventAttributes.Count > 0)
                        {
                            var lastKey = currentEventAttributes.Keys.Last();
                            currentEventAttributes[lastKey] += Environment.NewLine + line.TrimStart();
                        }
                    }
                }
            }
        }

        // Save final event
        if (currentEvent != null)
        {
            events.Add(currentEvent with { Attributes = currentEventAttributes });
        }

        // Validate required fields
        if (string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(spanId) || string.IsNullOrEmpty(name))
        {
            _logger.LogDebug("Skipping span block {BlockNumber} in {FileName}: missing required fields (TraceId={TraceId}, SpanId={SpanId}, Name={Name})",
                blockNumber, fileName, traceId ?? "null", spanId ?? "null", name ?? "null");
            return null;
        }

        return new Span
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentId = parentId ?? "0000000000000000",
            Name = name,
            Duration = duration,
            Status = status,
            Timestamp = timestamp,
            Tags = tags,
            Events = events
        };
    }

    private static TimeSpan ParseDuration(string value)
    {
        // Format: "177.2474ms" or "1.2624ms"
        var match = DurationRegex().Match(value);
        if (match.Success && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var ms))
        {
            return TimeSpan.FromMilliseconds(ms);
        }
        return TimeSpan.Zero;
    }

    private static SpanStatus ParseStatus(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "ok" => SpanStatus.Ok,
            "error" => SpanStatus.Error,
            _ => SpanStatus.Unset
        };
    }

    private enum Section
    {
        None,
        Tags,
        Events
    }

    [GeneratedRegex(@"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^\s*\[(\d{2}:\d{2}:\d{2}\.\d+)\]\s*(.+)$")]
    private static partial Regex EventHeaderRegex();

    [GeneratedRegex(@"^([\d.]+)ms$")]
    private static partial Regex DurationRegex();
}
