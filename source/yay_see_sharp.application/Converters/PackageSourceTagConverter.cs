using System.Globalization;
using Avalonia.Data.Converters;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.Converters;

/// <summary>Renders a repo-tag string for a <see cref="PackageSource"/>. Left untranslated (AUR/Official are effectively proper nouns in this domain).</summary>
public sealed class PackageSourceTagConverter : IValueConverter
{
    public static readonly PackageSourceTagConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PackageSource.Aur => "AUR",
        PackageSource.Official => "Official",
        PackageSource.Foreign => "Foreign",
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
