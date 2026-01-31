# Copilot Instructions for OTFE

## Project Overview

OTFE (OpenTelemetry File Explorer) is a WPF desktop application that opens and visualizes OpenTelemetry trace files. It stitches multiple trace files together into a unified view with a top-level event list, Gantt chart visualization, and detailed event inspection.

## Build & Test Commands

```bash
# Build the solution
dotnet build OTFE.slnx

# Run the application
dotnet run --project OTFE/OTFE.csproj

# Run all tests
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests in a specific class
dotnet test --filter "FullyQualifiedName~ClassName"
```

## Architecture

### Supported File Formats
- **`.log` files**: Custom C# Logger format with structured trace blocks (see `/Samples/*.log`)
- **`.jsonl` files**: JSON Lines format for OpenTelemetry data

### Trace File Structure (Log Format)
Each trace block contains:
- `TraceId`, `SpanId`, `ParentId` for correlation
- `Name`, `Duration`, `Status` for overview
- `Tags` with key-value pairs (e.g., `db.system.name`, `http.request.method`)

## Conventions

### C# Style
- PascalCase for all members
- 4 spaces for indentation
- Braces on new lines
- Nullable reference types enabled

### Patterns
- **MVVM**: Use CommunityToolkit.Mvvm for view models
- **DI**: Constructor injection only; register services in `Program.cs`
- **DTOs**: Use `record` types for data transfer objects
- **Async**: Use `async/await` throughout; never use `.Result` or `.Wait()`
- **Logging**: Use `ILogger<T>` for structured logging; no `Console.WriteLine`

### Testing
- xUnit with Moq for mocking
- Follow AAA pattern (Arrange, Act, Assert)
- Write tests for each completed task
- Mock all external dependencies

## Key Requirements Reference

The full specification is in `/Spec/OPFE.md`. Key features include:
- Multi-file trace stitching into unified views
- Search by tag criteria (e.g., `Status: Error`, `Duration > 500ms`)
- Sort by trace/event duration
- Anomaly detection using local ML
