using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace OTFE.Services;

/// <summary>
/// Watches a folder for trace file changes with debouncing.
/// </summary>
public class FileWatcherService : IFileWatcherService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingOperations = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private readonly string[] _supportedExtensions = [".log", ".jsonl"];

    private FileSystemWatcher? _watcher;
    private bool _isEnabled = true;
    private bool _disposed;

    public event EventHandler<FileChangedEventArgs>? FileCreated;
    public event EventHandler<FileChangedEventArgs>? FileChanged;
    public event EventHandler<FileChangedEventArgs>? FileDeleted;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = value;
            }
            _logger.LogInformation("File watching {Status}", value ? "enabled" : "disabled");
        }
    }

    public string? WatchedPath { get; private set; }

    public FileWatcherService(ILogger<FileWatcherService> logger)
    {
        _logger = logger;
    }

    public void StartWatching(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            _logger.LogWarning("Cannot watch folder: {Path}", folderPath);
            return;
        }

        StopWatching();

        WatchedPath = folderPath;
        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = _isEnabled
        };

        // Watch for supported extensions
        foreach (var ext in _supportedExtensions)
        {
            _watcher.Filters.Add($"*{ext}");
        }

        _watcher.Created += OnFileCreated;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnError;

        _logger.LogInformation("Started watching folder: {Path}", folderPath);
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Changed -= OnFileChanged;
            _watcher.Deleted -= OnFileDeleted;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;

            _logger.LogInformation("Stopped watching folder: {Path}", WatchedPath);
        }

        // Cancel any pending operations
        foreach (var cts in _pendingOperations.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _pendingOperations.Clear();

        WatchedPath = null;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedFile(e.FullPath)) return;
        _logger.LogDebug("File created detected: {Path}", e.FullPath);
        DebounceOperation(e.FullPath, () => FileCreated?.Invoke(this, new FileChangedEventArgs { FilePath = e.FullPath }));
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedFile(e.FullPath)) return;
        _logger.LogDebug("File changed detected: {Path}", e.FullPath);
        DebounceOperation(e.FullPath, () => FileChanged?.Invoke(this, new FileChangedEventArgs { FilePath = e.FullPath }));
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedFile(e.FullPath)) return;
        _logger.LogDebug("File deleted detected: {Path}", e.FullPath);
        
        // Cancel any pending operations for this file
        if (_pendingOperations.TryRemove(e.FullPath, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        FileDeleted?.Invoke(this, new FileChangedEventArgs { FilePath = e.FullPath });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Treat rename as delete old + create new
        if (IsSupportedFile(e.OldFullPath))
        {
            _logger.LogDebug("File renamed from: {Path}", e.OldFullPath);
            FileDeleted?.Invoke(this, new FileChangedEventArgs { FilePath = e.OldFullPath });
        }

        if (IsSupportedFile(e.FullPath))
        {
            _logger.LogDebug("File renamed to: {Path}", e.FullPath);
            DebounceOperation(e.FullPath, () => FileCreated?.Invoke(this, new FileChangedEventArgs { FilePath = e.FullPath }));
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error");
    }

    private void DebounceOperation(string filePath, Action action)
    {
        // Cancel any existing pending operation for this file
        if (_pendingOperations.TryRemove(filePath, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _pendingOperations[filePath] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceDelay, cts.Token);

                if (!cts.Token.IsCancellationRequested)
                {
                    _pendingOperations.TryRemove(filePath, out _);
                    action();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when debounce is cancelled
            }
            finally
            {
                cts.Dispose();
            }
        });
    }

    private bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return _supportedExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}
