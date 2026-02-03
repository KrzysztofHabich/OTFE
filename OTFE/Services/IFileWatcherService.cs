namespace OTFE.Services;

/// <summary>
/// Service for watching a folder for trace file changes.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Event raised when a new trace file is created.
    /// </summary>
    event EventHandler<FileChangedEventArgs>? FileCreated;

    /// <summary>
    /// Event raised when an existing trace file is modified.
    /// </summary>
    event EventHandler<FileChangedEventArgs>? FileChanged;

    /// <summary>
    /// Event raised when a trace file is deleted.
    /// </summary>
    event EventHandler<FileChangedEventArgs>? FileDeleted;

    /// <summary>
    /// Gets or sets whether watching is enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets the currently watched folder path, or null if not watching.
    /// </summary>
    string? WatchedPath { get; }

    /// <summary>
    /// Starts watching the specified folder for trace file changes.
    /// </summary>
    void StartWatching(string folderPath);

    /// <summary>
    /// Stops watching the current folder.
    /// </summary>
    void StopWatching();
}

/// <summary>
/// Event arguments for file change events.
/// </summary>
public class FileChangedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
}
