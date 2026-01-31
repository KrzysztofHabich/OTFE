namespace OTFE.Models;

/// <summary>
/// Represents a loaded trace file.
/// </summary>
public class TraceFile
{
    public required string FilePath { get; init; }
    public required TraceFileType FileType { get; init; }
    public IReadOnlyList<Span> Spans { get; set; } = [];

    public string FileName => System.IO.Path.GetFileName(FilePath);
    public int SpanCount => Spans.Count;
}

/// <summary>
/// Supported trace file formats.
/// </summary>
public enum TraceFileType
{
    Log,
    Jsonl
}
