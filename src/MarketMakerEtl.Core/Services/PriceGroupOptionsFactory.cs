using System.Globalization;
using MarketMakerEtl.Core.Models.PriceGroups;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Core.Services;

public static class PriceGroupOptionsFactory
{
    private const decimal DefaultSellerFeeRate = 0.10m;
    private const decimal DefaultSellerFeeFixed = 0m;
    private const decimal DefaultBuyerFeeRate = 0.036m;

    public static PriceGroupOptions Build(IConfiguration? configuration) =>
        new(
            ReadDecimal(configuration, "PriceGroups:SellerFeeRate", DefaultSellerFeeRate),
            ReadDecimal(configuration, "PriceGroups:SellerFeeFixed", DefaultSellerFeeFixed),
            ReadDecimal(configuration, "PriceGroups:BuyerFeeRate", DefaultBuyerFeeRate));

    private static decimal ReadDecimal(IConfiguration? configuration, string key, decimal fallback)
    {
        var value = configuration?[key];
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
