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
    public void Should_apply_scraper_client_defaults_when_configuration_is_empty()
    {
        var options = Resolve<ScrapeClientOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options.BaseUrl, Is.EqualTo("http://localhost:7126"));
            Assert.That(options.ApiKey, Is.Empty);
            Assert.That(options.FetchTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(options.PollInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void Should_apply_content_store_defaults_when_configuration_is_empty()
    {
        var options = Resolve<ScrapeContentOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options.ConnectionString, Is.EqualTo("UseDevelopmentStorage=true"));
            Assert.That(options.ContainerName, Is.EqualTo("html"));
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
