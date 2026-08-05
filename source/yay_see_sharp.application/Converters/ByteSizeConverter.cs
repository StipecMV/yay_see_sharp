using System.Globalization;
using Avalonia.Data.Converters;

namespace yay_see_sharp.application.Converters;

/// <summary>Formats a byte count as a compact human-readable size, e.g. 182_000_000 → "182 MB".</summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public static readonly ByteSizeConverter Instance = new();

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long and not int)
        {
            return string.Empty;
        }

        double size = System.Convert.ToInt64(value);
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        var format = unitIndex == 0 || size >= 10 ? "0" : "0.#";
        return $"{size.ToString(format, CultureInfo.InvariantCulture)} {Units[unitIndex]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
