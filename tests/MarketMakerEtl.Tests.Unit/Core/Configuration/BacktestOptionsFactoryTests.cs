using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class BacktestOptionsFactoryTests
{
    [Test]
    public void Should_apply_default_horizon_days_when_configuration_is_empty()
    {
        var options = BacktestOptionsFactory.Build(new ConfigurationBuilder().Build());

        Assert.That(options.HorizonDays, Is.EqualTo(14));
    }

    [Test]
    public void Should_read_horizon_days_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backtest:HorizonDays"] = "21"
        }).Build();

        var options = BacktestOptionsFactory.Build(configuration);

        Assert.That(options.HorizonDays, Is.EqualTo(21));
    }

    [Test]
    public void Should_apply_defaults_when_configuration_is_null()
    {
        var options = BacktestOptionsFactory.Build(null);

        Assert.That(options.HorizonDays, Is.EqualTo(14));
    }
}
