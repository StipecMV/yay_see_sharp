using System.Globalization;
using Avalonia.Data.Converters;

namespace yay_see_sharp.application.Converters;

/// <summary>Formats an AUR vote count as "★ 1,840 votes · ", or empty when the package has no vote data (official repos).</summary>
public sealed class VotesConverter : IValueConverter
{
    public static readonly VotesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int votes ? $"★ {votes:N0} votes · " : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
