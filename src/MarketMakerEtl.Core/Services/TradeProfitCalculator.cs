using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

public static class TradeProfitCalculator
{
    public static decimal ComputeBuyFees(decimal buyPrice, decimal? buyFees, PriceGroupOptions options) =>
        buyFees ?? buyPrice * options.BuyerFeeRate;

    public static decimal ComputeSellFees(decimal sellPrice, decimal? sellFees, PriceGroupOptions options) =>
        sellFees ?? (sellPrice * options.SellerFeeRate) + options.SellerFeeFixed;

    public static decimal ComputeRealisedProfit(
        decimal sellPrice,
        decimal sellShipping,
        decimal sellFees,
        decimal buyPrice,
        decimal buyShipping,
        decimal buyFees) =>
        sellPrice - sellShipping - sellFees - (buyPrice + buyShipping + buyFees);

    public static double ComputeDaysToSell(DateTime boughtUtc, DateTime soldUtc) =>
        (soldUtc - boughtUtc).TotalDays;
}
