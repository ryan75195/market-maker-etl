using System.Globalization;

namespace MarketMakerEtl.Core.Services;

internal static class SoldDateParser
{
    private const string Iso8601UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] EbayFormats =
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

        var trimmed = value.Trim();

        return ParseIso8601Utc(trimmed) ?? ParseEbayFormat(trimmed);
    }

    private static DateTime? ParseIso8601Utc(string value) =>
        DateTime.TryParseExact(
            value,
            Iso8601UtcFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static DateTime? ParseEbayFormat(string value) =>
        DateTime.TryParseExact(
            value,
            EbayFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
}
