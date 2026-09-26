using System.Globalization;
using MarketMakerEtl.Core.Models.Deals;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Core.Services;

public static class BacktestOptionsFactory
{
    private const int DefaultHorizonDays = 14;

    public static BacktestOptions Build(IConfiguration? configuration) =>
        new(ReadInt(configuration, "Backtest:HorizonDays", DefaultHorizonDays));

    private static int ReadInt(IConfiguration? configuration, string key, int fallback)
    {
        var value = configuration?[key];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
