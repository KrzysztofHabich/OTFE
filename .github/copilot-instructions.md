# Copilot Instructions for OTFE

## Project Overview

OTFE (OpenTelemetry File Explorer) is a WPF desktop application (.NET 10) that opens and visualizes OpenTelemetry trace files. It stitches multiple trace files together into a unified view with Gantt chart visualization and ML-based anomaly detection.

## Build & Test Commands

```bash
dotnet build OTFE.slnx                              # Build solution
dotnet run --project OTFE/OTFE.csproj               # Run application
dotnet test                                          # Run all tests
dotnet test --filter "FullyQualifiedName~TestName"  # Run single test
dotnet test --filter "FullyQualifiedName~ClassName" # Run test class
```

## Architecture

### Project Structure
- **OTFE/**: Main WPF application
  - `Models/`: Domain records (`Span`, `Trace`, `SpanEvent`, `TraceFile`)
  - `ViewModels/`: MVVM view models using CommunityToolkit.Mvvm
  - `Services/`: Business logic (`ITraceService`, `ISearchService`, `IAnomalyDetectionService`)
  - `Parsers/`: File parsers implementing `ITraceParser` (LogFileParser, JsonlFileParser)
  - `Controls/`: Custom WPF controls (GanttChartControl)
  - `Converters/`: WPF value converters
- **test/OTFE.Tests/**: xUnit tests with sample .log files in `Samples/`

### Key Interfaces
- `ITraceParser`: File parsing (`ParseAsync` returns `IReadOnlyList<Span>`)
- `ITraceService`: Load files, stitch spans into traces by TraceId
- `ISearchService`: Query parsing and filtering (supports `Status:Error`, `Duration>500ms`, `HasAnomalies`)
- `IAnomalyDetectionService`: ML.NET-based duration anomaly detection
- `IFileWatcherService`: Folder monitoring with debounced file change events

### Data Flow
1. Files loaded via `ITraceService.LoadFilesAsync()` → parsers selected by extension
2. Spans stitched into `Trace` objects via `StitchTraces()` (grouped by TraceId, linked by ParentId)
3. Root spans identified by `ParentId == "0000000000000000"`
4. Folder monitoring: `IFileWatcherService` watches for new/changed/deleted files (500ms debounce)

### File Formats
- **`.log`**: Custom structured format with `TraceId`, `SpanId`, `ParentId`, `Tags:`, `Events:` sections
- **`.jsonl`**: JSON Lines OpenTelemetry format

## Conventions

### C# Style
- PascalCase for all members; 4 spaces; braces on new lines
- Nullable reference types enabled throughout

### Patterns
- **MVVM**: CommunityToolkit.Mvvm for view models
- **DI**: Constructor injection; register in `App.xaml.cs` `ConfigureServices()`
- **DTOs**: Use `record` types (see `Span`, `SpanEvent`, `TraceFilter`)
- **Async**: Always `async/await`; never `.Result` or `.Wait()`
- **Logging**: `ILogger<T>` only; no `Console.WriteLine`

### Testing
- xUnit + Moq; AAA pattern
- Sample trace files in `test/OTFE.Tests/Samples/`
