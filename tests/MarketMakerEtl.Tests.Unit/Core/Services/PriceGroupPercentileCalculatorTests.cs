using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PriceGroupPercentileCalculatorTests
{
    [Test]
    public void Should_return_null_for_an_empty_list()
    {
        Assert.That(PriceGroupPercentileCalculator.Percentile([], 0.5), Is.Null);
    }

    [Test]
    public void Should_return_the_only_value_for_a_single_item_list()
    {
        var values = new List<decimal> { 42m };

        Assert.Multiple(() =>
        {
            Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.5), Is.EqualTo(42m));
            Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.25), Is.EqualTo(42m));
            Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.75), Is.EqualTo(42m));
        });
    }

    [Test]
    public void Should_return_the_middle_value_for_the_median_of_an_odd_count()
    {
        var values = new List<decimal> { 5m, 1m, 3m, 2m, 4m };

        Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.5), Is.EqualTo(3m));
    }

    [Test]
    public void Should_average_the_two_middle_values_for_the_median_of_an_even_count()
    {
        var values = new List<decimal> { 4m, 1m, 3m, 2m };

        Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.5), Is.EqualTo(2.5m));
    }

    [Test]
    public void Should_interpolate_p25_and_p75_linearly_between_order_statistics()
    {
        var values = new List<decimal> { 1m, 2m, 3m, 4m, 5m };

        Assert.Multiple(() =>
        {
            Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.25), Is.EqualTo(2m));
            Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.75), Is.EqualTo(4m));
        });
    }

    [Test]
    public void Should_interpolate_between_order_statistics_when_the_rank_is_fractional()
    {
        var values = new List<decimal> { 10m, 20m, 30m, 40m };

        Assert.That(PriceGroupPercentileCalculator.Percentile(values, 0.25), Is.EqualTo(17.5m));
    }
}
