using System.IO;
using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Services;

namespace OTFE.Tests.Services;

public class FileWatcherServiceTests : IDisposable
{
    private readonly Mock<ILogger<FileWatcherService>> _loggerMock;
    private readonly FileWatcherService _service;
    private readonly string _testFolder;

    public FileWatcherServiceTests()
    {
        _loggerMock = new Mock<ILogger<FileWatcherService>>();
        _service = new FileWatcherService(_loggerMock.Object);
        _testFolder = Path.Combine(Path.GetTempPath(), $"OTFE_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFolder);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testFolder))
        {
            try { Directory.Delete(_testFolder, true); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StartWatching_SetsWatchedPath()
    {
        // Arrange & Act
        _service.StartWatching(_testFolder);

        // Assert
        Assert.Equal(_testFolder, _service.WatchedPath);
    }

    [Fact]
    public void StopWatching_ClearsWatchedPath()
    {
        // Arrange
        _service.StartWatching(_testFolder);

        // Act
        _service.StopWatching();

        // Assert
        Assert.Null(_service.WatchedPath);
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue()
    {
        // Assert
        Assert.True(_service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        // Act
        _service.IsEnabled = false;

        // Assert
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void StartWatching_WithInvalidPath_DoesNotThrow()
    {
        // Arrange
        var invalidPath = Path.Combine(_testFolder, "nonexistent");

        // Act & Assert (should not throw)
        _service.StartWatching(invalidPath);
        Assert.Null(_service.WatchedPath);
    }

    [Fact]
    public void StartWatching_CalledTwice_UpdatesPath()
    {
        // Arrange
        var folder1 = Path.Combine(_testFolder, "folder1");
        var folder2 = Path.Combine(_testFolder, "folder2");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        // Act
        _service.StartWatching(folder1);
        _service.StartWatching(folder2);

        // Assert
        Assert.Equal(folder2, _service.WatchedPath);
    }

    [Fact]
    public async Task FileCreated_DoesNotRaiseEvent_ForUnsupportedExtension()
    {
        // Arrange
        var eventRaised = false;
        _service.FileCreated += (s, e) => eventRaised = true;
        _service.StartWatching(_testFolder);

        // Act
        var testFile = Path.Combine(_testFolder, "test.txt");
        await File.WriteAllTextAsync(testFile, "test content");

        // Assert - wait a bit to ensure no event
        await Task.Delay(1000);
        Assert.False(eventRaised, "FileCreated should not be raised for .txt files");
    }

    [Fact]
    public async Task IsEnabled_False_PreventsEvents()
    {
        // Arrange
        var eventRaised = false;
        _service.FileCreated += (s, e) => eventRaised = true;
        _service.StartWatching(_testFolder);
        _service.IsEnabled = false;

        // Act
        var testFile = Path.Combine(_testFolder, "test.log");
        await File.WriteAllTextAsync(testFile, "content");

        // Assert
        await Task.Delay(1000);
        Assert.False(eventRaised, "No events should be raised when disabled");
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act & Assert (should not throw)
        _service.Dispose();
        _service.Dispose();
    }

    [Fact]
    public void Dispose_StopsWatching()
    {
        // Arrange
        _service.StartWatching(_testFolder);
        Assert.NotNull(_service.WatchedPath);

        // Act
        _service.Dispose();

        // Assert
        Assert.Null(_service.WatchedPath);
    }
}
