using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OTFE.Models;

namespace OTFE.Converters;

/// <summary>
/// Converts SpanStatus to a color brush for status indicators.
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SpanStatus status)
        {
            return status switch
            {
                SpanStatus.Error => new SolidColorBrush(Color.FromRgb(220, 53, 69)),   // Red
                SpanStatus.Ok => new SolidColorBrush(Color.FromRgb(40, 167, 69)),      // Green
                _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))                  // Gray
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts SpanStatus to a text representation.
/// </summary>
public class StatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SpanStatus status)
        {
            return status switch
            {
                SpanStatus.Error => "⚠ Error",
                SpanStatus.Ok => "✓ Ok",
                _ => "○ Unset"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
