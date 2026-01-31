# OTFE Implementation Plan

## Problem Statement

Build a WPF application that opens multiple OpenTelemetry trace files (.log and .jsonl), stitches them together into a unified view, and provides visualization (Gantt charts), search, sorting, and ML-based anomaly detection.

## Approach

Build incrementally starting with the application shell and core data models, then add file parsing, UI components, and advanced features. Use custom WPF controls for visualization, CommunityToolkit.Mvvm for MVVM pattern, and ML.NET (local inference) for anomaly detection.

---

## Workplan

### Phase 1: Application Shell & Core Infrastructure ✅
- [x] **1.1** Set up project structure with proper folders (Models, ViewModels, Views, Services, Parsers)
- [x] **1.2** Add NuGet dependencies (CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging)
- [x] **1.3** Configure DI container in App.xaml.cs
- [x] **1.4** Create MainWindowViewModel with basic navigation structure
- [x] **1.5** Design main window layout (file list panel, trace list panel, detail panel)

### Phase 2: Core Data Models ✅
- [x] **2.1** Create `Span` record (TraceId, SpanId, ParentId, Name, Duration, Status, StartTime, Tags, Events)
- [x] **2.2** Create `SpanEvent` record (Timestamp, Name, Attributes)
- [x] **2.3** Create `Trace` class (TraceId, RootSpan, AllSpans, TotalDuration, Status)
- [x] **2.4** Create `TraceFile` class (FilePath, LoadedSpans, FileType)
- [x] **2.5** Write unit tests for models

### Phase 3: File Parsing ✅
- [x] **3.1** Create `ITraceParser` interface
- [x] **3.2** Implement `LogFileParser` for .log format (parse sample structure with Tags, Events, multiline support)
- [x] **3.3** Implement `JsonlFileParser` for .jsonl format
- [x] **3.4** Create `TraceParserFactory` to select parser by file extension
- [x] **3.5** Implement streaming/chunked parsing for large files
- [x] **3.6** Write unit tests for parsers using sample files

### Phase 4: Trace Stitching & Management ✅
- [x] **4.1** Create `ITraceService` interface
- [x] **4.2** Implement `TraceService` to manage loaded files and spans
- [x] **4.3** Implement trace stitching logic (group spans by TraceId, build parent-child hierarchy)
- [x] **4.4** Implement multi-file merging (combine spans from multiple files into unified traces)
- [x] **4.5** Write unit tests for trace stitching

### Phase 5: File Loading UI ✅
- [x] **5.1** Create file open dialog (single and multiple file selection)
- [x] **5.2** Create folder open dialog for bulk loading
- [x] **5.3** Implement async file loading with progress indication
- [x] **5.4** Display loaded files list with file info (name, span count, status)
- [x] **5.5** Add file remove/clear functionality

### Phase 6: Trace List View ✅
- [x] **6.1** Create `TraceListViewModel`
- [x] **6.2** Build trace list UI showing: Entry point (root span name), Total duration, Status, Span count
- [x] **6.3** Implement virtualization for large trace counts
- [x] **6.4** Add visual indicators (error highlighting, duration color coding)
- [x] **6.5** Implement trace selection

### Phase 7: Gantt Chart Visualization ✅
- [x] **7.1** Create custom `GanttChartControl` (WPF UserControl)
- [x] **7.2** Implement span bar rendering with proper timing/positioning
- [x] **7.3** Implement chronological display (sorted by timestamp)
- [x] **7.4** Add zoom and pan functionality (Ctrl+wheel zoom, wheel pan)
- [x] **7.5** Add span selection (click anywhere on row to select)
- [x] **7.6** Add visual indicators (error spans in red, status colors)

### Phase 8: Span Detail View ✅
- [x] **8.1** Create `SpanDetailViewModel`
- [x] **8.2** Build detail panel showing all span properties
- [x] **8.3** Display Tags as key-value list
- [x] **8.4** Display Events with timestamps and attributes
- [x] **8.5** Display anomaly indicators when detected

### Phase 9: Search & Filtering ✅
- [x] **9.1** Create `ISearchService` interface
- [x] **9.2** Implement search query parser (support: `Status:Error`, `Duration>500ms`, `tag.name=value`, `HasError`)
- [x] **9.3** Build search UI with query input
- [x] **9.4** Implement filter application to trace list
- [x] **9.5** Add quick filters (Errors only, Slow traces, Anomalies)
- [x] **9.6** Write unit tests for search query parsing

### Phase 10: Sorting ✅
- [x] **10.1** Add sort options to trace list (by duration, by timestamp, by status, by name, by span count)
- [x] **10.2** Implement ascending/descending toggle
- [x] **10.3** Add sort combo box to UI

### Phase 11: Navigation ✅
- [x] **11.1** Implement trace-to-trace navigation (next/previous commands)
- [x] **11.2** Add keyboard shortcuts (Ctrl+O open, Alt+Up/Down navigate traces)

### Phase 12: ML Anomaly Detection ✅
- [x] **12.1** Add ML.NET NuGet packages (Microsoft.ML, Microsoft.ML.TimeSeries)
- [x] **12.2** Create `IAnomalyDetectionService` interface
- [x] **12.3** Implement feature extraction (duration stats per span name)
- [x] **12.4** Implement duration anomaly detection (statistical + IID Spike Detection)
- [x] **12.5** Implement pattern comparison (same span name across traces)
- [x] **12.6** Create anomaly result model and UI indicators
- [x] **12.7** Add anomaly indicators in span detail view
- [x] **12.8** Write unit tests for anomaly detection logic

### Phase 13: Polish & Performance ✅
- [x] **13.1** Add error handling throughout application
- [x] **13.2** Add logging via ILogger<T>
- [x] **13.3** Status bar with loading and analysis progress indicators
- [x] **13.4** 51 unit tests passing

---

## Notes

### Log File Format Structure
```
[TIMESTAMP] TRACE
TraceId: {hex}
SpanId: {hex}
ParentId: {hex or 0000000000000000 for root}
Name: {span name}
Duration: {value}ms
Status: {Ok|Error|Unset}
Tags:
  {key} = {value}
  ...
Events:
  [{time}] {event name}
    {attribute} = {value}
--------------------------------------------------------------------------------
```

### Key Technical Decisions
- Root spans identified by `ParentId: 0000000000000000`
- Traces grouped by `TraceId`, spans linked by `ParentId`
- ML.NET runs locally (no cloud dependency)
- Custom Gantt control for full control over visualization
- Virtualization required for large datasets

### Dependencies
- CommunityToolkit.Mvvm 8.*
- Microsoft.Extensions.DependencyInjection 10.*
- Microsoft.Extensions.Logging.* 10.*
- Microsoft.ML 4.0.0
- Microsoft.ML.TimeSeries 4.0.0

### Build & Test Commands
```powershell
dotnet build
dotnet test
dotnet run --project OTFE
```
