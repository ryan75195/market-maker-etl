using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class PriceGroupOptionsFactoryTests
{
    [Test]
    public void Should_apply_default_fee_config_when_configuration_is_empty()
    {
        var options = PriceGroupOptionsFactory.Build(new ConfigurationBuilder().Build());

        Assert.Multiple(() =>
        {
            Assert.That(options.SellerFeeRate, Is.EqualTo(0.10m));
            Assert.That(options.SellerFeeFixed, Is.EqualTo(0.50m));
        });
    }

    [Test]
    public void Should_read_seller_fee_rate_and_fixed_fee_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PriceGroups:SellerFeeRate"] = "0.12",
            ["PriceGroups:SellerFeeFixed"] = "0.30"
        }).Build();

        var options = PriceGroupOptionsFactory.Build(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(options.SellerFeeRate, Is.EqualTo(0.12m));
            Assert.That(options.SellerFeeFixed, Is.EqualTo(0.30m));
        });
    }

    [Test]
    public void Should_apply_default_fee_config_when_configuration_is_null()
    {
        var options = PriceGroupOptionsFactory.Build(null);

        Assert.Multiple(() =>
        {
            Assert.That(options.SellerFeeRate, Is.EqualTo(0.10m));
            Assert.That(options.SellerFeeFixed, Is.EqualTo(0.50m));
        });
    }
}
