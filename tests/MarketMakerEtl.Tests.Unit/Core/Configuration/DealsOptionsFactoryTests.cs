using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class DealsOptionsFactoryTests
{
    [Test]
    public void Should_apply_default_tick_minutes_and_no_webhook_when_configuration_is_empty()
    {
        var options = DealsOptionsFactory.Build(new ConfigurationBuilder().Build());

        Assert.Multiple(() =>
        {
            Assert.That(options.TickMinutes, Is.EqualTo(10));
            Assert.That(options.WebhookUrl, Is.Null);
        });
    }

    [Test]
    public void Should_read_tick_minutes_and_webhook_url_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deals:TickMinutes"] = "15",
            ["Deals:WebhookUrl"] = "https://example.test/webhook"
        }).Build();

        var options = DealsOptionsFactory.Build(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(options.TickMinutes, Is.EqualTo(15));
            Assert.That(options.WebhookUrl, Is.EqualTo("https://example.test/webhook"));
        });
    }

    [Test]
    public void Should_apply_defaults_when_configuration_is_null()
    {
        var options = DealsOptionsFactory.Build(null);

        Assert.Multiple(() =>
        {
            Assert.That(options.TickMinutes, Is.EqualTo(10));
            Assert.That(options.WebhookUrl, Is.Null);
        });
    }
}
