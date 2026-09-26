namespace MarketMakerEtl.Core.Services;

public static class DealDiscountCalculator
{
    public static decimal Calculate(decimal soldNetMedian, decimal landedPrice) =>
        soldNetMedian == 0m ? 0m : (soldNetMedian - landedPrice) / soldNetMedian;

    public static bool MeetsThreshold(int soldCount, decimal discount, int minSold, decimal minDiscount) =>
        soldCount >= minSold && discount >= minDiscount;
}
