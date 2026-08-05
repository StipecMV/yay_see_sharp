using System.Globalization;
using Avalonia.Data.Converters;

namespace yay_see_sharp.application.Converters;

/// <summary>Compares a bound value against ConverterParameter by string representation. Handy for enum-driven IsVisible toggles in XAML.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
