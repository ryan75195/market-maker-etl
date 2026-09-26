using System.Globalization;
using MarketMakerEtl.Core.Models.Deals;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Core.Services;

public static class DealsOptionsFactory
{
    private const int DefaultTickMinutes = 10;

    public static DealsOptions Build(IConfiguration? configuration) =>
        new(
            ReadInt(configuration, "Deals:TickMinutes", DefaultTickMinutes),
            ReadOptionalString(configuration, "Deals:WebhookUrl"));

    private static int ReadInt(IConfiguration? configuration, string key, int fallback)
    {
        var value = configuration?[key];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static string? ReadOptionalString(IConfiguration? configuration, string key)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
