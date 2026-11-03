using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MiniPaint.App.Controls;

/// <summary>Highlights the active toolbox button (CurrentTool → accent brush).</summary>
public sealed class ToolHighlightConverter : IValueConverter
{
    public static ToolHighlightConverter Instance { get; } = new();

    private static readonly IBrush Active = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C));
    private static readonly IBrush Idle = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CanvasTool tool && parameter is string name &&
            Enum.TryParse<CanvasTool>(name, ignoreCase: true, out var expected) &&
            tool == expected)
            return Active;
        return Idle;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
