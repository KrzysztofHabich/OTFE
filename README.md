# OTFE

***OTFE (OpenTelemetry File Explorer)*** is a WPF desktop application that opens and visualizes OpenTelemetry trace files. It stitches multiple trace files together into a unified view with a top-level event list, Gantt chart visualization, and detailed event inspection.


![OTFE main interface displaying a unified trace view with an event list on the left showing multiple spans, a Gantt chart in the center visualizing span timelines and dependencies, and a details panel on the right showing trace metadata including TraceId, SpanId, and tag information. The application presents a professional, organized layout for analyzing OpenTelemetry trace data across multiple files.](images/OTFE-Screenshot.png?raw=true)

### Smart Search & Filtering
  Search supports compound search queries with AND.
  - HasError AND Name:GET Dashboard               - Errors containing "GET Dashboard"
  - Status:Error AND Duration>500ms               - Errors slower than 500ms
  - HasError AND Name:GET AND Duration>100ms      - Multiple conditions combined
  - HasAnomalies
  - HasAnomalies AND Name:GET
  - HasError AND HasAnomalies


### Supported File Formats
- **`.jsonl` files**: JSON Lines format for OpenTelemetry data
- **`.log` files**: C# Logger format with structured trace blocks 
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
```


### Trace File Structure (Log Format)
Each trace block contains:
- `TraceId`, `SpanId`, `ParentId` for correlation
- `Name`, `Duration`, `Status` for overview
- `Tags` with key-value pairs (e.g., `db.system.name`, `http.request.method`)
- `Events` with timestamps and attributes