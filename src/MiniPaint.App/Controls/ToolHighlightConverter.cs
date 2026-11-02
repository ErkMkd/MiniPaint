using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MiniPaint.App.Controls;

/// <summary>Placeholder converter kept for XAML extensibility; buttons use commands.</summary>
public sealed class ToolHighlightConverter : IValueConverter
{
    public static ToolHighlightConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CanvasTool tool && parameter is string name &&
            Enum.TryParse<CanvasTool>(name, out var expected) && tool == expected)
            return new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C));
        return new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
