using Microsoft.Extensions.Logging;
using Moq;
using OTFE.Models;
using OTFE.Services;

namespace OTFE.Tests.Services;

public class SearchServiceTests
{
    private readonly SearchService _searchService;

    public SearchServiceTests()
    {
        var logger = new Mock<ILogger<SearchService>>();
        _searchService = new SearchService(logger.Object);
    }

    [Fact]
    public void ParseQuery_EmptyQuery_ReturnsEmptyFilter()
    {
        // Arrange & Act
        var filter = _searchService.ParseQuery("");

        // Assert
        Assert.True(filter.IsEmpty);
    }

    [Theory]
    [InlineData("Status:Error", SpanStatus.Error)]
    [InlineData("Status:Ok", SpanStatus.Ok)]
    [InlineData("Status:Unset", SpanStatus.Unset)]
    [InlineData("status:error", SpanStatus.Error)]
    public void ParseQuery_StatusFilter_ParsesCorrectly(string query, SpanStatus expectedStatus)
    {
        // Act
        var filter = _searchService.ParseQuery(query);

        // Assert
        Assert.Equal(expectedStatus, filter.Status);
    }

    [Fact]
    public void ParseQuery_DurationGreaterThan_ParsesCorrectly()
    {
        // Act
        var filter = _searchService.ParseQuery("Duration>500ms");

        // Assert
        Assert.Equal(500, filter.MinDurationMs);
    }

    [Fact]
    public void ParseQuery_DurationLessThan_ParsesCorrectly()
    {
        // Act
        var filter = _searchService.ParseQuery("Duration<100");

        // Assert
        Assert.Equal(100, filter.MaxDurationMs);
    }

    [Fact]
    public void ParseQuery_HasError_ParsesCorrectly()
    {
        // Act
        var filter = _searchService.ParseQuery("HasError");

        // Assert
        Assert.True(filter.HasError);
    }

    [Fact]
    public void ParseQuery_NameFilter_ParsesCorrectly()
    {
        // Act
        var filter = _searchService.ParseQuery("Name:SELECT");

        // Assert
        Assert.Equal("SELECT", filter.NameContains);
    }

    [Fact]
    public void ParseQuery_TagFilter_ParsesCorrectly()
    {
        // Act
        var filter = _searchService.ParseQuery("db.system.name=microsoft.sql_server");

        // Assert
        Assert.Contains("db.system.name", filter.TagFilters.Keys);
        Assert.Equal("microsoft.sql_server", filter.TagFilters["db.system.name"]);
    }

    [Fact]
    public void FilterTraces_StatusFilter_FiltersCorrectly()
    {
        // Arrange
        var traces = CreateTestTraces();
        var filter = new TraceFilter { Status = SpanStatus.Error };

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert
        Assert.All(result, t => Assert.Equal(SpanStatus.Error, t.Status));
    }

    [Fact]
    public void FilterTraces_MinDurationFilter_FiltersCorrectly()
    {
        // Arrange
        var traces = CreateTestTraces();
        var filter = new TraceFilter { MinDurationMs = 200 };

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert
        Assert.All(result, t => Assert.True(t.TotalDuration.TotalMilliseconds >= 200));
    }

    [Fact]
    public void FilterTraces_EmptyFilter_ReturnsAllTraces()
    {
        // Arrange
        var traces = CreateTestTraces();
        var filter = new TraceFilter();

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert
        Assert.Equal(traces.Count, result.Count);
    }

    [Fact]
    public void FilterTraces_NameFilter_FiltersCorrectly()
    {
        // Arrange
        var traces = CreateTestTraces();
        var filter = new TraceFilter { NameContains = "GET" };

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert
        Assert.All(result, t => Assert.Contains(t.AllSpans, s => s.Name.Contains("GET")));
    }

    [Fact]
    public void ParseQuery_AndOperator_CombinesFilters()
    {
        // Act
        var filter = _searchService.ParseQuery("HasError AND Name:GET");

        // Assert
        Assert.True(filter.HasError);
        Assert.Equal("GET", filter.NameContains);
    }

    [Fact]
    public void ParseQuery_AndOperator_CaseInsensitive()
    {
        // Act
        var filter = _searchService.ParseQuery("HasError and Name:Dashboard");

        // Assert
        Assert.True(filter.HasError);
        Assert.Equal("Dashboard", filter.NameContains);
    }

    [Fact]
    public void ParseQuery_MultipleAndConditions_CombinesAll()
    {
        // Act
        var filter = _searchService.ParseQuery("HasError AND Name:GET AND Duration>100ms");

        // Assert
        Assert.True(filter.HasError);
        Assert.Equal("GET", filter.NameContains);
        Assert.Equal(100, filter.MinDurationMs);
    }

    [Fact]
    public void FilterTraces_AndOperator_FiltersCorrectly()
    {
        // Arrange - traces with different combinations
        var traces = new List<Trace>
        {
            CreateTrace("trace1", "GET /api/users", SpanStatus.Error, TimeSpan.FromMilliseconds(100)),
            CreateTrace("trace2", "GET /api/data", SpanStatus.Ok, TimeSpan.FromMilliseconds(500)),
            CreateTrace("trace3", "POST /api/items", SpanStatus.Error, TimeSpan.FromMilliseconds(250)),
        };

        // Filter for: HasError AND Name contains "GET"
        var filter = _searchService.ParseQuery("HasError AND Name:GET");

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert - only trace1 matches both conditions
        Assert.Single(result);
        Assert.Equal("trace1", result[0].TraceId);
    }

    [Fact]
    public void FilterTraces_ThreeConditionsWithAnd_FiltersCorrectly()
    {
        // Arrange
        var traces = new List<Trace>
        {
            CreateTrace("trace1", "GET /api/users", SpanStatus.Error, TimeSpan.FromMilliseconds(300)),
            CreateTrace("trace2", "GET /api/data", SpanStatus.Error, TimeSpan.FromMilliseconds(50)),
            CreateTrace("trace3", "POST /api/items", SpanStatus.Error, TimeSpan.FromMilliseconds(400)),
        };

        // Filter for: HasError AND Name:GET AND Duration>100ms
        var filter = _searchService.ParseQuery("HasError AND Name:GET AND Duration>100ms");

        // Act
        var result = _searchService.FilterTraces(traces, filter);

        // Assert - only trace1 matches all three conditions
        Assert.Single(result);
        Assert.Equal("trace1", result[0].TraceId);
    }

    private static List<Trace> CreateTestTraces()
    {
        return
        [
            CreateTrace("trace1", "GET /api/users", SpanStatus.Ok, TimeSpan.FromMilliseconds(100)),
            CreateTrace("trace2", "POST /api/data", SpanStatus.Error, TimeSpan.FromMilliseconds(500)),
            CreateTrace("trace3", "GET /api/items", SpanStatus.Ok, TimeSpan.FromMilliseconds(250)),
        ];
    }

    private static Trace CreateTrace(string traceId, string name, SpanStatus status, TimeSpan duration)
    {
        var rootSpan = new Span
        {
            TraceId = traceId,
            SpanId = "span1",
            ParentId = "0000000000000000",
            Name = name,
            Status = status,
            Duration = duration,
            Timestamp = DateTime.UtcNow
        };

        return new Trace
        {
            TraceId = traceId,
            RootSpan = rootSpan,
            AllSpans = [rootSpan]
        };
    }
}
