using System.Globalization;

namespace yay_see_sharp.infrastructure.Yay;

/// <summary>Shared "Installed Size" field parser (e.g. "1.20 MiB") used by both the `-Qi`/`-Si` detail parser and the statistics size aggregation, so the two never drift apart on rounding/units.</summary>
internal static class PacmanSizeParser
{
    public static long ParseToBytes(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return 0;
        }

        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "KIB" => 1024d,
            "MIB" => 1024d * 1024d,
            "GIB" => 1024d * 1024d * 1024d,
            _ => 1d,
        };
        return (long)(number * multiplier);
    }
}
