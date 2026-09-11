using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class ContentStoreConfigurationTests
{
    [Test]
    public void Should_read_content_store_connection_string_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ContentStore:ConnectionString"] = "UseDevelopmentStorage=true;AccountName=configured"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.ConnectionString, Is.EqualTo("UseDevelopmentStorage=true;AccountName=configured"));
    }

    [Test]
    public void Should_read_content_store_container_name_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ContentStore:ContainerName"] = "scraped-pages"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.ContainerName, Is.EqualTo("scraped-pages"));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ScrapeContentOptions ResolveOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddCoreServices(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ScrapeContentOptions>();
    }
}
