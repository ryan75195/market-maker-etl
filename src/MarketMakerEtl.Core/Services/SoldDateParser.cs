using System.Globalization;

namespace MarketMakerEtl.Core.Services;

internal static class SoldDateParser
{
    private static readonly string[] Formats =
    [
        "ddd, d MMM 'at' h:mm tt",
        "ddd, dd MMM 'at' h:mm tt",
        "d MMMM yyyy",
        "d MMM yyyy",
    ];

    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            Formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }
}
