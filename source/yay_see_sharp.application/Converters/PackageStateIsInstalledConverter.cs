using System.Globalization;
using Avalonia.Data.Converters;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.Converters;

/// <summary>True when a package is installed or has an update pending. Pass ConverterParameter="invert" to flip the result.</summary>
public sealed class PackageStateIsInstalledConverter : IValueConverter
{
    public static readonly PackageStateIsInstalledConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isInstalled = value is PackageState.Installed or PackageState.UpdateAvailable;
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !isInstalled : isInstalled;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
