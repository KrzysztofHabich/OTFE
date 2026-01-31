using OTFE.Models;

namespace OTFE.Services;

/// <summary>
/// Interface for managing traces loaded from files.
/// </summary>
public interface ITraceService
{
    /// <summary>
    /// Loads trace files and returns the loaded TraceFile objects.
    /// </summary>
    Task<IReadOnlyList<TraceFile>> LoadFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stitches spans from all loaded files into complete traces.
    /// </summary>
    IReadOnlyList<Trace> StitchTraces(IEnumerable<TraceFile> traceFiles);

    /// <summary>
    /// Clears all loaded data.
    /// </summary>
    void Clear();
}
