using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DealDiscountCalculatorTests
{
    [TestCase(100, 80, 0.20)]
    [TestCase(100, 100, 0.00)]
    [TestCase(100, 120, -0.20)]
    [TestCase(200, 150, 0.25)]
    public void Should_compute_discount_as_the_relative_gap_from_sold_net_median(
        decimal soldNetMedian, decimal landedPrice, decimal expected)
    {
        var discount = DealDiscountCalculator.Calculate(soldNetMedian, landedPrice);

        Assert.That(discount, Is.EqualTo(expected));
    }

    [Test]
    public void Should_return_zero_discount_when_sold_net_median_is_zero()
    {
        var discount = DealDiscountCalculator.Calculate(0m, 10m);

        Assert.That(discount, Is.EqualTo(0m));
    }

    [TestCase(5, 0.20, 5, 0.20, true)]
    [TestCase(4, 0.20, 5, 0.20, false)]
    [TestCase(5, 0.19, 5, 0.20, false)]
    [TestCase(10, 0.50, 5, 0.20, true)]
    public void Should_meet_threshold_only_when_sold_count_and_discount_both_clear_the_bar(
        int soldCount, decimal discount, int minSold, decimal minDiscount, bool expected)
    {
        var meetsThreshold = DealDiscountCalculator.MeetsThreshold(soldCount, discount, minSold, minDiscount);

        Assert.That(meetsThreshold, Is.EqualTo(expected));
    }
}
