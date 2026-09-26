using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Core;

internal static class ConfigurationValueReader
{
    public static string ReadString(IConfiguration? configuration, string key, string fallback)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string? ReadOptionalString(IConfiguration? configuration, string key)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static int ReadInt(IConfiguration? configuration, string key, int fallback)
    {
        var value = configuration?[key];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static bool ReadBool(IConfiguration? configuration, string key, bool fallback)
    {
        var value = configuration?[key];
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public static double ReadDouble(IConfiguration? configuration, string key, double fallback)
    {
        var value = configuration?[key];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
