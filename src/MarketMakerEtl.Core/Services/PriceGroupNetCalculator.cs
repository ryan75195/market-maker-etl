using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

public static class PriceGroupNetCalculator
{
    private const string BuyerPaysShipping = "buyer";
    private const string SellerPaysShipping = "seller";

    public static decimal ComputeLandedPrice(decimal price, string? shippingPayer, decimal? shippingCost) =>
        IsBuyerPaid(shippingPayer) && shippingCost.HasValue ? price + shippingCost.Value : price;

    public static decimal ComputeNetProceeds(
        decimal soldPrice, string? shippingPayer, decimal? shippingCost, PriceGroupOptions options)
    {
        var afterFees = soldPrice - (soldPrice * options.SellerFeeRate) - options.SellerFeeFixed;
        return IsSellerPaid(shippingPayer) && shippingCost.HasValue ? afterFees - shippingCost.Value : afterFees;
    }

    public static bool IsShippingPayerUnknown(string? shippingPayer) =>
        !IsBuyerPaid(shippingPayer) && !IsSellerPaid(shippingPayer);

    private static bool IsBuyerPaid(string? shippingPayer) =>
        string.Equals(shippingPayer, BuyerPaysShipping, StringComparison.OrdinalIgnoreCase);

    private static bool IsSellerPaid(string? shippingPayer) =>
        string.Equals(shippingPayer, SellerPaysShipping, StringComparison.OrdinalIgnoreCase);
}
