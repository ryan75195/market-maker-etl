using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class ScrapeLimitsConfigurationTests
{
    [Test]
    public void Should_read_max_pages_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scrape:MaxPages"] = "7"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.MaxPages, Is.EqualTo(7));
    }

    [Test]
    public void Should_read_collect_sold_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scrape:CollectSold"] = "false"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.CollectSold, Is.False);
    }

    [Test]
    public void Should_read_sold_backfill_days_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scrape:SoldBackfillDays"] = "14"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.SoldBackfillDays, Is.EqualTo(14));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ScrapeOptions ResolveOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddCoreServices(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ScrapeOptions>();
    }
}
