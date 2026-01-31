using System.IO;
using Microsoft.Extensions.Logging;
using OTFE.Models;
using OTFE.Parsers;

namespace OTFE.Services;

/// <summary>
/// Service for loading and managing traces.
/// </summary>
public class TraceService : ITraceService
{
    private readonly ILogger<TraceService> _logger;
    private readonly TraceParserFactory _parserFactory;

    public TraceService(ILogger<TraceService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _parserFactory = new TraceParserFactory(loggerFactory);
    }

    public async Task<IReadOnlyList<TraceFile>> LoadFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var traceFiles = new List<TraceFile>();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var parser = _parserFactory.GetParser(filePath);
                if (parser == null)
                {
                    _logger.LogWarning("No parser found for file: {FilePath}", filePath);
                    continue;
                }

                _logger.LogInformation("Parsing file: {FilePath}", filePath);
                var spans = await parser.ParseAsync(filePath, cancellationToken);

                // Set the SourceFile on each span
                var spansWithSource = spans.Select(s => s with { SourceFile = filePath }).ToList();

                var fileType = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".log" => TraceFileType.Log,
                    ".jsonl" => TraceFileType.Jsonl,
                    _ => TraceFileType.Log
                };

                var traceFile = new TraceFile
                {
                    FilePath = filePath,
                    FileType = fileType,
                    Spans = spansWithSource
                };

                traceFiles.Add(traceFile);
                _logger.LogInformation("Loaded {SpanCount} spans from {FilePath}", spans.Count, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading file: {FilePath}", filePath);
            }
        }

        return traceFiles;
    }

    public IReadOnlyList<Trace> StitchTraces(IEnumerable<TraceFile> traceFiles)
    {
        // Collect all spans from all files
        var allSpans = traceFiles
            .SelectMany(f => f.Spans)
            .ToList();

        _logger.LogInformation("Stitching {SpanCount} spans into traces", allSpans.Count);

        // Group spans by TraceId
        var spansByTraceId = allSpans
            .GroupBy(s => s.TraceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var traces = new List<Trace>();

        foreach (var (traceId, spans) in spansByTraceId)
        {
            // Find root span (no parent or parent is zeros)
            var rootSpan = spans.FirstOrDefault(s => s.IsRoot);

            if (rootSpan == null)
            {
                // If no explicit root, find span whose parent is not in this trace
                var spanIds = spans.Select(s => s.SpanId).ToHashSet();
                rootSpan = spans.FirstOrDefault(s => !spanIds.Contains(s.ParentId));

                if (rootSpan == null)
                {
                    // Fallback: use the earliest span
                    rootSpan = spans.OrderBy(s => s.Timestamp).First();
                    _logger.LogWarning("No root span found for trace {TraceId}, using earliest span", traceId);
                }
            }

            var trace = new Trace
            {
                TraceId = traceId,
                RootSpan = rootSpan,
                AllSpans = spans
            };

            traces.Add(trace);
        }

        _logger.LogInformation("Created {TraceCount} traces", traces.Count);
        return traces.OrderByDescending(t => t.RootSpan.Timestamp).ToList();
    }

    public void Clear()
    {
        _logger.LogInformation("Cleared all trace data");
    }
}
