using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OTFE.Models;

namespace OTFE.Controls;

/// <summary>
/// Custom control for displaying a Gantt chart of spans within a trace.
/// </summary>
public class GanttChartControl : Control
{
    private const double RowHeight = 24;
    private const double LabelWidth = 200;
    private const double MinBarWidth = 4;
    private const double ChartPadding = 4;

    static GanttChartControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GanttChartControl),
            new FrameworkPropertyMetadata(typeof(GanttChartControl)));
    }

    public static readonly DependencyProperty TraceProperty =
        DependencyProperty.Register(nameof(Trace), typeof(Trace), typeof(GanttChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedSpanProperty =
        DependencyProperty.Register(nameof(SelectedSpan), typeof(Span), typeof(GanttChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(GanttChartControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(GanttChartControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Trace? Trace
    {
        get => (Trace?)GetValue(TraceProperty);
        set => SetValue(TraceProperty, value);
    }

    public Span? SelectedSpan
    {
        get => (Span?)GetValue(SelectedSpanProperty);
        set => SetValue(SelectedSpanProperty, value);
    }

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public static readonly RoutedEvent SpanSelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(SpanSelected), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<Span?>), typeof(GanttChartControl));

    public event RoutedPropertyChangedEventHandler<Span?> SpanSelected
    {
        add => AddHandler(SpanSelectedEvent, value);
        remove => RemoveHandler(SpanSelectedEvent, value);
    }

    private List<(Rect RowBounds, Rect BarBounds, Span Span)>? _spanRects;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (Trace == null)
        {
            DrawEmptyState(dc);
            return;
        }

        _spanRects = [];
        var flattenedSpans = FlattenSpanHierarchy(Trace);

        if (flattenedSpans.Count == 0)
        {
            DrawEmptyState(dc);
            return;
        }

        var minTimestamp = flattenedSpans.Min(s => s.Span.Timestamp);
        var maxEndTime = flattenedSpans.Max(s => s.Span.Timestamp + s.Span.Duration);
        var totalDuration = (maxEndTime - minTimestamp).TotalMilliseconds;

        if (totalDuration <= 0)
        {
            totalDuration = 1;
        }

        var chartWidth = (ActualWidth - LabelWidth - ChartPadding * 2) * ZoomLevel;
        var pixelsPerMs = chartWidth / totalDuration;

        // Draw background
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Draw header line
        dc.DrawLine(new Pen(Brushes.LightGray, 1), new Point(0, RowHeight), new Point(ActualWidth, RowHeight));

        // Draw column separator
        dc.DrawLine(new Pen(Brushes.LightGray, 1),
            new Point(LabelWidth, 0), new Point(LabelWidth, ActualHeight));

         // Draw spans
        var y = RowHeight + ChartPadding;
        var rowIndex = 0;

        foreach (var (span, depth) in flattenedSpans)
        {
            var startOffset = (span.Timestamp - minTimestamp).TotalMilliseconds * pixelsPerMs - HorizontalOffset;
            var barWidth = Math.Max(span.Duration.TotalMilliseconds * pixelsPerMs, MinBarWidth);

            // Draw row background (selected, alternate colors)
            Brush rowBrush;
            if (span == SelectedSpan)
            {
                rowBrush = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // Light blue for selection
            }
            else
            {
                rowBrush = rowIndex % 2 == 0
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(248, 248, 248));
            }
            dc.DrawRectangle(rowBrush, null, new Rect(0, y, ActualWidth, RowHeight));

            // Draw label with indentation
            var labelText = new FormattedText(
                span.Name,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                span == SelectedSpan ? Brushes.DodgerBlue : Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            labelText.MaxTextWidth = LabelWidth - (depth * 12) - 8;
            labelText.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(labelText, new Point(ChartPadding + depth * 12, y + (RowHeight - labelText.Height) / 2));

            // Draw span bar
            var barBrush = GetSpanBrush(span);
            var barRect = new Rect(
                LabelWidth + ChartPadding + startOffset,
                y + 4,
                barWidth,
                RowHeight - 8);

            // Clip to visible area
            if (barRect.Right > LabelWidth && barRect.Left < ActualWidth)
            {
                dc.DrawRoundedRectangle(barBrush, null, barRect, 2, 2);

                // Draw selection highlight on bar
                if (span == SelectedSpan)
                {
                    dc.DrawRoundedRectangle(null, new Pen(Brushes.DodgerBlue, 2), barRect, 2, 2);
                }

                // Draw duration text on bar if it fits
                if (barWidth > 60)
                {
                    var durationText = new FormattedText(
                        $"{span.Duration.TotalMilliseconds:F1}ms",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        9,
                        Brushes.White,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);

                    dc.DrawText(durationText, new Point(
                        barRect.Left + 4,
                        barRect.Top + (barRect.Height - durationText.Height) / 2));
                }
            }

            // Store row bounds for click detection (entire row is clickable)
            var rowRect = new Rect(0, y, ActualWidth, RowHeight);
            _spanRects.Add((rowRect, barRect, span));
            y += RowHeight;
            rowIndex++;
        }

        // Draw time ruler at top
        DrawTimeRuler(dc, minTimestamp, totalDuration, pixelsPerMs);
    }

    private static void DrawEmptyState(DrawingContext dc)
    {
        var text = new FormattedText(
            "Select a trace to view timeline",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            Brushes.Gray,
            96);
        // Text will be drawn at center - just draw placeholder
    }

    private void DrawTimeRuler(DrawingContext dc, DateTime minTime, double totalDurationMs, double pixelsPerMs)
    {
        var tickCount = Math.Max(2, (int)((ActualWidth - LabelWidth) / 100));
        var msPerTick = totalDurationMs / tickCount;

        for (int i = 0; i <= tickCount; i++)
        {
            var ms = i * msPerTick;
            var x = LabelWidth + ChartPadding + ms * pixelsPerMs - HorizontalOffset;

            if (x >= LabelWidth && x <= ActualWidth)
            {
                dc.DrawLine(new Pen(Brushes.LightGray, 1),
                    new Point(x, 0), new Point(x, RowHeight));

                var timeText = new FormattedText(
                    $"{ms:F0}ms",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    9,
                    Brushes.Gray,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(timeText, new Point(x + 2, 4));
            }
        }
    }

    private static Brush GetSpanBrush(Span span)
    {
        return span.Status switch
        {
            SpanStatus.Error => new SolidColorBrush(Color.FromRgb(220, 53, 69)),   // Red
            SpanStatus.Ok => new SolidColorBrush(Color.FromRgb(40, 167, 69)),      // Green
            _ => new SolidColorBrush(Color.FromRgb(23, 162, 184))                   // Blue (Unset)
        };
    }

    private static List<(Span Span, int Depth)> FlattenSpanHierarchy(Trace trace)
    {
        // Sort all spans chronologically by start timestamp
        var sortedSpans = trace.AllSpans
            .OrderBy(s => s.Timestamp)
            .ToList();

        // Calculate depth based on parent-child relationships
        var depthMap = new Dictionary<string, int>();
        var result = new List<(Span, int)>();

        foreach (var span in sortedSpans)
        {
            int depth = 0;
            if (!span.IsRoot && depthMap.TryGetValue(span.ParentId, out var parentDepth))
            {
                depth = parentDepth + 1;
            }
            depthMap[span.SpanId] = depth;
            result.Add((span, depth));
        }

        return result;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_spanRects == null)
        {
            return;
        }

        var point = e.GetPosition(this);

        // Check if click is in the header area
        if (point.Y < RowHeight)
        {
            return;
        }

        // Find which row was clicked (label or bar - both are clickable)
        foreach (var (rowBounds, _, span) in _spanRects)
        {
            if (rowBounds.Contains(point))
            {
                var oldValue = SelectedSpan;
                SelectedSpan = span;
                RaiseEvent(new RoutedPropertyChangedEventArgs<Span?>(oldValue, span, SpanSelectedEvent));
                break;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_spanRects == null)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        var point = e.GetPosition(this);

        // Check if mouse is over a span row (not header)
        if (point.Y > RowHeight)
        {
            foreach (var (rowBounds, _, _) in _spanRects)
            {
                if (rowBounds.Contains(point))
                {
                    Cursor = Cursors.Hand;
                    return;
                }
            }
        }

        Cursor = Cursors.Arrow;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Zoom
            var zoomDelta = e.Delta > 0 ? 1.2 : 0.8;
            ZoomLevel = Math.Clamp(ZoomLevel * zoomDelta, 0.1, 10.0);
            e.Handled = true;
        }
        else
        {
            // Pan
            HorizontalOffset = Math.Max(0, HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
