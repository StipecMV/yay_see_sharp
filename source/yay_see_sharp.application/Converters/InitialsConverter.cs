using System.Globalization;
using Avalonia.Data.Converters;

namespace yay_see_sharp.application.Converters;

/// <summary>Derives a 2-letter monogram from a package name for the icon placeholder tiles (e.g. "visual-studio-code-bin" → "VS", "firefox" → "FF").</summary>
public sealed class InitialsConverter : IValueConverter
{
    public static readonly InitialsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || name.Length == 0)
        {
            return string.Empty;
        }

        var segments = name.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = segments.Length >= 2
            ? $"{segments[0][0]}{segments[1][0]}"
            : name.Length >= 2 ? name[..2] : name;

        return initials.ToUpperInvariant();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
