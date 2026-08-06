using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.Converters;

/// <summary>UI-22: maps a toast's <see cref="NotificationLevel"/> to its accent brush (green/red/purple for Success/Error/Info+Warning).</summary>
public sealed class NotificationLevelBrushConverter : IValueConverter
{
    public static readonly NotificationLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            NotificationLevel.Success => "Brush.Success",
            NotificationLevel.Error => "Brush.Destructive",
            _ => "Brush.Accent",
        };

        return Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) == true
            ? resource as IBrush
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
