using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OTFE.Models;

namespace OTFE.Services;

/// <summary>
/// Service for searching and filtering traces.
/// Supports AND operator to combine conditions: "HasError AND Name:GET Dashboard"
/// </summary>
public partial class SearchService : ISearchService
{
    private readonly ILogger<SearchService> _logger;
    private HashSet<string> _anomalousTraceIds = [];

    public SearchService(ILogger<SearchService> logger)
    {
        _logger = logger;
    }

    public void SetAnomalousTraceIds(IReadOnlyList<string> traceIds)
    {
        _anomalousTraceIds = [..traceIds];
        _logger.LogDebug("Updated anomalous trace IDs: {Count} traces", _anomalousTraceIds.Count);
    }

    public TraceFilter ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new TraceFilter();
        }

        // Split by AND (case insensitive) and parse each part
        var parts = AndSplitRegex().Split(query)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        // Start with an empty filter and merge all parts
        var filter = new TraceFilter { RawQuery = query };
        var tagFilters = new Dictionary<string, string>();

        foreach (var part in parts)
        {
            var partFilter = ParseSingleCondition(part);
            filter = MergeFilters(filter, partFilter);
            
            // Accumulate tag filters
            foreach (var (key, value) in partFilter.TagFilters)
            {
                tagFilters[key] = value;
            }
        }

        if (tagFilters.Count > 0)
        {
            filter = filter with { TagFilters = tagFilters };
        }

        _logger.LogDebug("Parsed query '{Query}' into filter: Status={Status}, MinDuration={MinDuration}, MaxDuration={MaxDuration}, Name={Name}, HasError={HasError}, Tags={TagCount}",
            query, filter.Status, filter.MinDurationMs, filter.MaxDurationMs, filter.NameContains, filter.HasError, filter.TagFilters.Count);

        return filter;
    }

    private static TraceFilter ParseSingleCondition(string condition)
    {
        var filter = new TraceFilter();
        var tagFilters = new Dictionary<string, string>();

        // Parse Status:Error or Status:Ok
        var statusMatch = StatusRegex().Match(condition);
        if (statusMatch.Success)
        {
            var statusValue = statusMatch.Groups[1].Value.ToLowerInvariant();
            filter = filter with
            {
                Status = statusValue switch
                {
                    "error" => SpanStatus.Error,
                    "ok" => SpanStatus.Ok,
                    "unset" => SpanStatus.Unset,
                    _ => null
                }
            };
        }

        // Parse Duration>500ms or Duration<100ms or Duration>=500 or Duration<=100
        var durationMatch = DurationRegex().Match(condition);
        if (durationMatch.Success)
        {
            var op = durationMatch.Groups[1].Value;
            if (double.TryParse(durationMatch.Groups[2].Value, out var durationValue))
            {
                filter = op switch
                {
                    ">" or ">=" => filter with { MinDurationMs = durationValue },
                    "<" or "<=" => filter with { MaxDurationMs = durationValue },
                    _ => filter
                };
            }
        }

        // Parse Name:something or Name:"something with spaces"
        var nameMatch = NameRegex().Match(condition);
        if (nameMatch.Success)
        {
            var nameValue = nameMatch.Groups[1].Value.Trim('"');
            filter = filter with { NameContains = nameValue };
        }

        // Parse HasError or Errors
        if (HasErrorRegex().IsMatch(condition))
        {
            filter = filter with { HasError = true };
        }

        // Parse HasAnomalies or Anomalies or Anomaly
        if (HasAnomaliesRegex().IsMatch(condition))
        {
            filter = filter with { HasAnomalies = true };
        }

        // Parse tag filters like db.system.name=microsoft.sql_server or http.method=GET
        var tagMatches = TagRegex().Matches(condition);
        foreach (Match match in tagMatches)
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim('"');
            
            // Skip if this is a known filter keyword
            if (!key.Equals("Status", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("Duration", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                tagFilters[key] = value;
            }
        }

        if (tagFilters.Count > 0)
        {
            filter = filter with { TagFilters = tagFilters };
        }

        return filter;
    }

    private static TraceFilter MergeFilters(TraceFilter existing, TraceFilter newFilter)
    {
        return existing with
        {
            Status = newFilter.Status ?? existing.Status,
            HasError = newFilter.HasError || existing.HasError,
            HasAnomalies = newFilter.HasAnomalies || existing.HasAnomalies,
            MinDurationMs = newFilter.MinDurationMs ?? existing.MinDurationMs,
            MaxDurationMs = newFilter.MaxDurationMs ?? existing.MaxDurationMs,
            NameContains = !string.IsNullOrEmpty(newFilter.NameContains) ? newFilter.NameContains : existing.NameContains
        };
    }

    public IReadOnlyList<Trace> FilterTraces(IEnumerable<Trace> traces, TraceFilter filter)
    {
        if (filter.IsEmpty)
        {
            return traces.ToList();
        }

        var result = traces.Where(trace => MatchesFilter(trace, filter)).ToList();
        _logger.LogDebug("Filtered traces: {ResultCount} matches out of input", result.Count);
        return result;
    }

    private bool MatchesFilter(Trace trace, TraceFilter filter)
    {
        // Check status filter
        if (filter.Status != null && trace.Status != filter.Status)
        {
            return false;
        }

        // Check HasError filter
        if (filter.HasError && trace.Status != SpanStatus.Error)
        {
            return false;
        }

        // Check HasAnomalies filter
        if (filter.HasAnomalies && !_anomalousTraceIds.Contains(trace.TraceId))
        {
            return false;
        }

        // Check duration filters (on trace total duration)
        var durationMs = trace.TotalDuration.TotalMilliseconds;
        if (filter.MinDurationMs != null && durationMs < filter.MinDurationMs)
        {
            return false;
        }
        if (filter.MaxDurationMs != null && durationMs > filter.MaxDurationMs)
        {
            return false;
        }

        // Check name filter (on root span or any span)
        if (!string.IsNullOrEmpty(filter.NameContains))
        {
            var nameMatches = trace.AllSpans.Any(s => 
                s.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
            if (!nameMatches)
            {
                return false;
            }
        }

        // Check tag filters (any span in trace must match)
        foreach (var (key, value) in filter.TagFilters)
        {
            var tagMatches = trace.AllSpans.Any(s =>
                s.Tags.TryGetValue(key, out var tagValue) &&
                tagValue.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (!tagMatches)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\s+AND\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AndSplitRegex();

    [GeneratedRegex(@"Status[:\s]*(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"Duration\s*([><]=?)\s*(\d+(?:\.\d+)?)\s*(?:ms)?", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"Name[:\s]*""?([^""]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\b(?:HasError|Errors?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HasErrorRegex();

    [GeneratedRegex(@"\b(?:HasAnomal(?:y|ies)|Anomal(?:y|ies))\b", RegexOptions.IgnoreCase)]
    private static partial Regex HasAnomaliesRegex();

    [GeneratedRegex(@"([\w.]+)\s*=\s*""?([^""\s]+)""?")]
    private static partial Regex TagRegex();
}
