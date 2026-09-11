using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class SessionReferenceFromConfigurationTests
{
    [Test]
    public void Should_read_the_session_reference_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:SessionReference"] = "configured-session"
        });

        var options = ResolveOptions(configuration);

        Assert.That(options.SessionReference, Is.EqualTo("configured-session"));
    }

    [Test]
    public void Should_change_the_resolved_session_reference_when_configuration_changes()
    {
        var first = ResolveOptions(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:SessionReference"] = "first-session"
        }));
        var second = ResolveOptions(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Scraper:SessionReference"] = "second-session"
        }));

        Assert.Multiple(() =>
        {
            Assert.That(first.SessionReference, Is.EqualTo("first-session"));
            Assert.That(second.SessionReference, Is.EqualTo("second-session"));
        });
    }

    [Test]
    public void Should_leave_the_session_reference_unset_when_configuration_does_not_provide_it()
    {
        var options = ResolveOptions(BuildConfiguration(new Dictionary<string, string?>()));

        Assert.That(options.SessionReference, Is.Null.Or.Empty);
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
