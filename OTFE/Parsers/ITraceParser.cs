using OTFE.Models;

namespace OTFE.Parsers;

/// <summary>
/// Interface for parsing trace files into spans.
/// </summary>
public interface ITraceParser
{
    /// <summary>
    /// Parses a trace file and returns all spans found.
    /// </summary>
    /// <param name="filePath">Path to the trace file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of spans parsed from the file.</returns>
    Task<IReadOnlyList<Span>> ParseAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the file extensions this parser supports.
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }
}
