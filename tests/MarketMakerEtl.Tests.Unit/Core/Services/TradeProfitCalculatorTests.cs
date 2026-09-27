using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TradeProfitCalculatorTests
{
    private static readonly PriceGroupOptions Options = new(SellerFeeRate: 0.10m, SellerFeeFixed: 0.30m, BuyerFeeRate: 0.036m);

    [Test]
    public void Should_default_buy_fees_from_the_buyer_fee_rate_when_omitted()
    {
        var buyFees = TradeProfitCalculator.ComputeBuyFees(100m, 0m, null, Options);

        Assert.That(buyFees, Is.EqualTo(3.6m));
    }

    [Test]
    public void Should_default_buy_fees_from_the_buyer_fee_rate_applied_to_price_plus_shipping()
    {
        var buyFees = TradeProfitCalculator.ComputeBuyFees(100m, 20m, null, Options);

        Assert.That(buyFees, Is.EqualTo(4.32m));
    }

    [Test]
    public void Should_use_the_explicit_buy_fees_when_provided_instead_of_defaulting()
    {
        var buyFees = TradeProfitCalculator.ComputeBuyFees(100m, 0m, 9.99m, Options);

        Assert.That(buyFees, Is.EqualTo(9.99m));
    }

    [Test]
    public void Should_default_sell_fees_from_the_seller_rate_and_fixed_fee_when_omitted()
    {
        var sellFees = TradeProfitCalculator.ComputeSellFees(100m, null, Options);

        Assert.That(sellFees, Is.EqualTo(10.30m));
    }

    [Test]
    public void Should_use_the_explicit_sell_fees_when_provided_instead_of_defaulting()
    {
        var sellFees = TradeProfitCalculator.ComputeSellFees(100m, 5m, Options);

        Assert.That(sellFees, Is.EqualTo(5m));
    }

    [Test]
    public void Should_compute_realised_profit_as_net_sale_proceeds_minus_net_purchase_cost()
    {
        var profit = TradeProfitCalculator.ComputeRealisedProfit(
            sellPrice: 100m, sellShipping: 5m, sellFees: 10m,
            buyPrice: 50m, buyShipping: 3m, buyFees: 2m);

        Assert.That(profit, Is.EqualTo(30m));
    }

    [Test]
    public void Should_compute_a_negative_realised_profit_when_costs_exceed_sale_proceeds()
    {
        var profit = TradeProfitCalculator.ComputeRealisedProfit(
            sellPrice: 40m, sellShipping: 0m, sellFees: 4m,
            buyPrice: 50m, buyShipping: 0m, buyFees: 2m);

        Assert.That(profit, Is.EqualTo(-16m));
    }

    [Test]
    public void Should_compute_days_to_sell_as_the_span_between_bought_and_sold_dates()
    {
        var boughtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var soldUtc = new DateTime(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc);

        var days = TradeProfitCalculator.ComputeDaysToSell(boughtUtc, soldUtc);

        Assert.That(days, Is.EqualTo(3.5));
    }
}
