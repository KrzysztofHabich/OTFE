using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OTFE.Models;
using OTFE.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace OTFE.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ITraceService _traceService;
    private readonly ISearchService _searchService;
    private readonly IAnomalyDetectionService _anomalyService;

    private IReadOnlyList<Trace> _allTraces = [];
    private IReadOnlyList<AnomalyResult> _anomalies = [];

    [ObservableProperty]
    private ObservableCollection<TraceFile> _loadedFiles = [];

    [ObservableProperty]
    private ObservableCollection<TraceFile> _selectedFiles = [];

    [ObservableProperty]
    private ObservableCollection<Trace> _traces = [];

    [ObservableProperty]
    private Trace? _selectedTrace;

    [ObservableProperty]
    private Span? _selectedSpan;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private SortOption _selectedSortOption = SortOption.Timestamp;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _isAnalyzingAnomalies;

    [ObservableProperty]
    private int _anomalyCount;

    [ObservableProperty]
    private ObservableCollection<AnomalyResult> _currentSpanAnomalies = [];

    [ObservableProperty]
    private bool _hasFileSelection;

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger, 
        ITraceService traceService,
        ISearchService searchService,
        IAnomalyDetectionService anomalyService)
    {
        _logger = logger;
        _traceService = traceService;
        _searchService = searchService;
        _anomalyService = anomalyService;
        _logger.LogInformation("MainWindowViewModel initialized");
    }

    public IEnumerable<SortOption> SortOptions => Enum.GetValues<SortOption>();

    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Trace Files (*.log;*.jsonl)|*.log;*.jsonl|All Files (*.*)|*.*",
            Multiselect = true,
            Title = "Open Trace Files"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFilesAsync(dialog.FileNames);
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder with Trace Files"
        };

        if (dialog.ShowDialog() == true)
        {
            var files = Directory.GetFiles(dialog.FolderName, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await LoadFilesAsync(files);
        }
    }

    private async Task LoadFilesAsync(string[] filePaths)
    {
        if (filePaths.Length == 0)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = $"Loading {filePaths.Length} file(s)...";

        try
        {
            var newFiles = await _traceService.LoadFilesAsync(filePaths);

            foreach (var file in newFiles)
            {
                LoadedFiles.Add(file);
            }

            // Stitch all loaded files into traces
            _allTraces = _traceService.StitchTraces(LoadedFiles);
            ApplyFilterAndSort();

            // Run anomaly detection in background
            _ = DetectAnomaliesAsync();

            var totalSpans = LoadedFiles.Sum(f => f.SpanCount);
            StatusMessage = $"Loaded {LoadedFiles.Count} file(s), {totalSpans} spans, {_allTraces.Count} traces";
            _logger.LogInformation("Loaded {FileCount} files with {SpanCount} spans into {TraceCount} traces",
                LoadedFiles.Count, totalSpans, _allTraces.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading files");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        LoadedFiles.Clear();
        SelectedFiles.Clear();
        HasFileSelection = false;
        Traces.Clear();
        _allTraces = [];
        SelectedTrace = null;
        SelectedSpan = null;
        SearchQuery = "";
        _traceService.Clear();
        StatusMessage = "Cleared all files";
        _logger.LogInformation("Cleared all loaded files");
    }

    [RelayCommand]
    private void ClearFileSelection()
    {
        SelectedFiles.Clear();
        HasFileSelection = false;
        ApplyFilterAndSort();
        _logger.LogDebug("Cleared file selection, showing all traces");
    }

    public void OnFileSelectionChanged(IList<object> selectedItems)
    {
        SelectedFiles.Clear();
        foreach (var item in selectedItems)
        {
            if (item is TraceFile file)
            {
                SelectedFiles.Add(file);
            }
        }
        HasFileSelection = SelectedFiles.Count > 0;
        ApplyFilterAndSort();
        _logger.LogDebug("File selection changed: {Count} files selected", SelectedFiles.Count);
    }

    [RelayCommand]
    private void Search()
    {
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void FilterErrors()
    {
        SearchQuery = "HasError";
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void FilterSlowTraces()
    {
        SearchQuery = "Duration>500ms";
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void FilterAnomalies()
    {
        SearchQuery = "HasAnomalies";
        ApplyFilterAndSort();
    }

    [RelayCommand]
    private async Task RunAnomalyDetectionAsync()
    {
        await DetectAnomaliesAsync();
    }

    private async Task DetectAnomaliesAsync()
    {
        if (_allTraces.Count == 0) return;

        IsAnalyzingAnomalies = true;
        var prevStatus = StatusMessage;
        StatusMessage = "Analyzing traces for anomalies...";

        try
        {
            _anomalies = await _anomalyService.DetectAnomaliesAsync(_allTraces);
            AnomalyCount = _anomalies.Count;

            // Update search service with anomalous trace IDs
            var anomalousIds = _anomalyService.GetAnomalousTraceIds();
            _searchService.SetAnomalousTraceIds(anomalousIds);

            if (_anomalies.Count > 0)
            {
                StatusMessage = $"{prevStatus} • {_anomalies.Count} anomalies detected";
                _logger.LogInformation("Detected {Count} anomalies", _anomalies.Count);
            }
            else
            {
                StatusMessage = prevStatus;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anomaly detection");
            StatusMessage = prevStatus;
        }
        finally
        {
            IsAnalyzingAnomalies = false;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Debounce could be added here for better UX
        ApplyFilterAndSort();
    }

    partial void OnSelectedSortOptionChanged(SortOption value)
    {
        ApplyFilterAndSort();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        // Start with all traces or filter by selected files
        IEnumerable<Trace> tracesToFilter = _allTraces;
        
        if (HasFileSelection && SelectedFiles.Count > 0)
        {
            var selectedFilePaths = SelectedFiles.Select(f => f.FilePath).ToHashSet();
            tracesToFilter = _allTraces.Where(t => 
                t.AllSpans.Any(s => selectedFilePaths.Contains(s.SourceFile)));
        }

        var filter = _searchService.ParseQuery(SearchQuery);
        var filtered = _searchService.FilterTraces(tracesToFilter, filter);

        // Apply sorting
        var sorted = ApplySort(filtered);

        Traces.Clear();
        foreach (var trace in sorted)
        {
            Traces.Add(trace);
        }

        // Build status message
        var statusParts = new List<string>();
        statusParts.Add($"Showing {Traces.Count}");
        
        if (HasFileSelection)
        {
            statusParts.Add($"from {SelectedFiles.Count} file(s)");
        }
        
        if (!filter.IsEmpty)
        {
            statusParts.Add("(filtered)");
        }

        statusParts.Add($"of {_allTraces.Count} total traces");
        StatusMessage = string.Join(" ", statusParts);
    }

    private IEnumerable<Trace> ApplySort(IEnumerable<Trace> traces)
    {
        var sorted = SelectedSortOption switch
        {
            SortOption.Timestamp => SortDescending 
                ? traces.OrderByDescending(t => t.RootSpan.Timestamp)
                : traces.OrderBy(t => t.RootSpan.Timestamp),
            SortOption.Duration => SortDescending
                ? traces.OrderByDescending(t => t.TotalDuration)
                : traces.OrderBy(t => t.TotalDuration),
            SortOption.SpanCount => SortDescending
                ? traces.OrderByDescending(t => t.SpanCount)
                : traces.OrderBy(t => t.SpanCount),
            SortOption.Status => SortDescending
                ? traces.OrderByDescending(t => t.Status)
                : traces.OrderBy(t => t.Status),
            SortOption.Name => SortDescending
                ? traces.OrderByDescending(t => t.EntryPoint)
                : traces.OrderBy(t => t.EntryPoint),
            _ => traces
        };

        return sorted;
    }

    partial void OnSelectedTraceChanged(Trace? value)
    {
        SelectedSpan = value?.RootSpan;
        _logger.LogDebug("Selected trace: {TraceId}", value?.TraceId);
    }

    partial void OnSelectedSpanChanged(Span? value)
    {
        _logger.LogDebug("Selected span: {SpanId} - {Name}", value?.SpanId, value?.Name);
        UpdateCurrentSpanAnomalies();
    }

    private void UpdateCurrentSpanAnomalies()
    {
        CurrentSpanAnomalies.Clear();
        if (SelectedSpan == null) return;

        var spanAnomalies = _anomalies.Where(a => 
            a.TraceId == SelectedSpan.TraceId && a.SpanId == SelectedSpan.SpanId);

        foreach (var anomaly in spanAnomalies)
        {
            CurrentSpanAnomalies.Add(anomaly);
        }
    }

    [RelayCommand]
    private void SelectNextTrace()
    {
        if (Traces.Count == 0) return;
        
        var currentIndex = SelectedTrace != null ? Traces.IndexOf(SelectedTrace) : -1;
        var nextIndex = (currentIndex + 1) % Traces.Count;
        SelectedTrace = Traces[nextIndex];
    }

    [RelayCommand]
    private void SelectPreviousTrace()
    {
        if (Traces.Count == 0) return;
        
        var currentIndex = SelectedTrace != null ? Traces.IndexOf(SelectedTrace) : 0;
        var prevIndex = currentIndex <= 0 ? Traces.Count - 1 : currentIndex - 1;
        SelectedTrace = Traces[prevIndex];
    }
}

public enum SortOption
{
    Timestamp,
    Duration,
    SpanCount,
    Status,
    Name
}
