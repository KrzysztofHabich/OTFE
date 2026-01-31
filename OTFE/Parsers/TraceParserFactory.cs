using System.IO;
using Microsoft.Extensions.Logging;

namespace OTFE.Parsers;

/// <summary>
/// Factory for creating trace parsers based on file extension.
/// </summary>
public class TraceParserFactory
{
    private readonly IReadOnlyList<ITraceParser> _parsers;

    public TraceParserFactory(ILoggerFactory loggerFactory)
    {
        _parsers =
        [
            new LogFileParser(loggerFactory.CreateLogger<LogFileParser>()),
            new JsonlFileParser(loggerFactory.CreateLogger<JsonlFileParser>())
        ];
    }

    /// <summary>
    /// Gets the appropriate parser for the given file path based on its extension.
    /// </summary>
    /// <param name="filePath">Path to the trace file.</param>
    /// <returns>The appropriate parser, or null if no parser supports the file extension.</returns>
    public ITraceParser? GetParser(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        foreach (var parser in _parsers)
        {
            if (parser.SupportedExtensions.Contains(extension))
            {
                return parser;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    public IEnumerable<string> SupportedExtensions => _parsers.SelectMany(p => p.SupportedExtensions);
}
