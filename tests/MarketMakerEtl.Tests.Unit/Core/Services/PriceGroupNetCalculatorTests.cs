using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PriceGroupNetCalculatorTests
{
    private static readonly PriceGroupOptions Options = new(0.10m, 0.50m, 0m);
    private static readonly PriceGroupOptions DefaultOptions = new(0.10m, 0m, 0.036m);

    [Test]
    public void Should_add_shipping_cost_to_landed_price_when_buyer_pays()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, "buyer", 8m, Options);

        Assert.That(landed, Is.EqualTo(108m));
    }

    [Test]
    public void Should_not_add_shipping_cost_to_landed_price_when_seller_pays()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, "seller", 8m, Options);

        Assert.That(landed, Is.EqualTo(100m));
    }

    [Test]
    public void Should_not_add_shipping_cost_to_landed_price_when_the_payer_is_unknown()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, null, 8m, Options);

        Assert.That(landed, Is.EqualTo(100m));
    }

    [Test]
    public void Should_not_add_shipping_cost_to_landed_price_when_the_cost_is_missing()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, "buyer", null, Options);

        Assert.That(landed, Is.EqualTo(100m));
    }

    [Test]
    public void Should_apply_the_buyer_fee_to_item_price_and_buyer_paid_shipping()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, "buyer", 10m, DefaultOptions);

        Assert.That(landed, Is.EqualTo(113.96m));
    }

    [Test]
    public void Should_apply_the_buyer_fee_to_item_price_alone_when_seller_pays_shipping()
    {
        var landed = PriceGroupNetCalculator.ComputeLandedPrice(100m, "seller", 10m, DefaultOptions);

        Assert.That(landed, Is.EqualTo(103.6m));
    }

    [Test]
    public void Should_subtract_only_fees_from_net_proceeds_when_the_buyer_pays_shipping()
    {
        var net = PriceGroupNetCalculator.ComputeNetProceeds(100m, "buyer", 8m, Options);

        Assert.That(net, Is.EqualTo(89.5m));
    }

    [Test]
    public void Should_subtract_fees_and_shipping_cost_from_net_proceeds_when_the_seller_pays_shipping()
    {
        var net = PriceGroupNetCalculator.ComputeNetProceeds(100m, "seller", 8m, Options);

        Assert.That(net, Is.EqualTo(81.5m));
    }

    [Test]
    public void Should_subtract_only_fees_from_net_proceeds_when_the_payer_is_unknown()
    {
        var net = PriceGroupNetCalculator.ComputeNetProceeds(100m, null, 8m, Options);

        Assert.That(net, Is.EqualTo(89.5m));
    }

    [Test]
    public void Should_net_ninety_percent_of_a_hundred_dollar_sale_with_default_fee_config()
    {
        var net = PriceGroupNetCalculator.ComputeNetProceeds(100m, "buyer", 10m, DefaultOptions);

        Assert.That(net, Is.EqualTo(90m));
    }

    [Test]
    public void Should_honour_the_configured_fee_rate_and_fixed_fee()
    {
        var net = PriceGroupNetCalculator.ComputeNetProceeds(100m, "seller", null, new PriceGroupOptions(0.15m, 1m, 0m));

        Assert.That(net, Is.EqualTo(84m));
    }

    [TestCase("buyer", false)]
    [TestCase("seller", false)]
    [TestCase("Buyer", false)]
    [TestCase(null, true)]
    [TestCase("", true)]
    [TestCase("pickup", true)]
    public void Should_report_whether_the_shipping_payer_is_unknown(string? shippingPayer, bool expected)
    {
        Assert.That(PriceGroupNetCalculator.IsShippingPayerUnknown(shippingPayer), Is.EqualTo(expected));
    }
}
