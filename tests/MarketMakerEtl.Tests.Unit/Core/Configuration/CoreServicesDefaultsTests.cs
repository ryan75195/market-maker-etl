using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class CoreServicesDefaultsTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Test]
    public void Should_apply_fetcher_defaults_when_configuration_is_empty()
    {
        var options = Resolve<FetcherOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options.BaseUrl, Is.EqualTo("http://127.0.0.1:8766"));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public void Should_apply_scrape_defaults_when_configuration_is_empty()
    {
        var options = Resolve<ScrapeOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxPages, Is.EqualTo(2));
            Assert.That(options.CollectSold, Is.True);
        });
    }

    [Test]
    public void Should_default_sold_backfill_days_to_thirty()
    {
        var options = Resolve<ScrapeOptions>();

        Assert.That(options.SoldBackfillDays, Is.EqualTo(30));
    }

    [Test]
    public void Should_default_max_backfill_item_page_fetches_to_four_hundred()
    {
        var options = Resolve<ScrapeOptions>();

        Assert.That(options.MaxBackfillItemPageFetches, Is.EqualTo(400));
    }

    [Test]
    public void Should_default_search_concurrency_to_three()
    {
        var options = Resolve<ScrapeOptions>();

        Assert.That(options.SearchConcurrency, Is.EqualTo(3));
    }

    [Test]
    public void Should_apply_database_location_default_when_configuration_is_empty()
    {
        var services = new ServiceCollection();
        services.AddCoreServices(EmptyConfiguration);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        using var db = factory.CreateDbContext();

        var dataSource = db.Database.GetDbConnection().DataSource;

        Assert.That(Path.GetFileName(dataSource), Is.EqualTo("marketmakeretl.db"));
    }

    private static T Resolve<T>()
        where T : notnull
    {
        var services = new ServiceCollection();
        services.AddCoreServices(EmptyConfiguration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<T>();
    }
}
