using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class ScraperClientConfigurationTests
{
    [Test]
    public void Should_read_scraper_base_url_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:BaseUrl"] = "https://scraper.primary.test"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.BaseUrl, Is.EqualTo("https://scraper.primary.test"));
    }

    [Test]
    public void Should_read_scraper_api_key_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:ApiKey"] = "function-key-from-config"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.ApiKey, Is.EqualTo("function-key-from-config"));
    }

    [Test]
    public void Should_change_resolved_base_url_when_configuration_changes()
    {
        var first = ResolveOptions(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:BaseUrl"] = "https://scraper.first.test"
        }));
        var second = ResolveOptions(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:BaseUrl"] = "https://scraper.second.test"
        }));

        Assert.Multiple(() =>
        {
            Assert.That(first.BaseUrl, Is.EqualTo("https://scraper.first.test"));
            Assert.That(second.BaseUrl, Is.EqualTo("https://scraper.second.test"));
        });
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ScrapeClientOptions ResolveOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddCoreServices(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ScrapeClientOptions>();
    }
}
