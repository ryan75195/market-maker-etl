using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class BuyItNowPageExposesTitlePriceCurrencyConditionAndActiveStatusTests
{
    private const string ActiveBuyItNowPage = """
        <div class=x-item-title>
          <h1 class=x-item-title__mainTitle>Apple iPhone 15 Pro 256GB</h1>
        </div>
        <div class=x-price-primary>
          <span class=x-price-primary__price>£749.99</span>
        </div>
        <div class=x-bin-price>
          <span>Buy It Now</span>
        </div>
        <div class=x-item-condition-text>
          <span>Pre-owned</span>
        </div>
        """;

    [Test]
    public void Should_expose_the_title_price_currency_condition_and_active_status()
    {
        var listing = EbayItemPageParser.Parse(ActiveBuyItNowPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("Apple iPhone 15 Pro 256GB"));
            Assert.That(listing.Price, Is.EqualTo(749.99m));
            Assert.That(listing.Currency, Is.EqualTo("£"));
            Assert.That(listing.Condition, Is.EqualTo("Pre-owned"));
            Assert.That(listing.Status, Is.EqualTo("Active"));
        });
    }
}
