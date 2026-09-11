using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EbaySearchParserExposesConditionPrimaryImageAndBuyingFormatTests
{
    private const string CardWithAttributes = """
        <ul>
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=x">link</a>
            <img class="s-card__image" src="https://i.ebayimg.com/images/g/abc123/s-l500.jpg" />
            <span class="s-card__title">PlayStation 5 Slim</span>
            <span class="s-card__price">£249.99</span>
            <span class="s-card__condition">Pre-owned</span>
            <span class="s-card__buying-format">Buy It Now</span>
          </li>
        </ul>
        """;

    [Test]
    public void Should_expose_condition_primary_image_and_buying_format_from_a_card()
    {
        var summaries = new EbaySearchParser().Parse(CardWithAttributes);

        var summary = summaries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.Condition, Is.EqualTo("Pre-owned"));
            Assert.That(summary.PrimaryImageUrl, Is.EqualTo("https://i.ebayimg.com/images/g/abc123/s-l500.jpg"));
            Assert.That(summary.BuyingFormat, Is.EqualTo("Buy It Now"));
        });
    }
}
