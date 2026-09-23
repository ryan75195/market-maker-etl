using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class RealCardMarkupExtractsAllListingAttributesTests
{
    private const string FullRealEbayCard = """
        <ul class="srp-results">
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=item1">link</a>
            <div class="s-card__image">
              <img alt="Sony PlayStation 5 Slim" src="https://i.ebayimg.com/images/g/xyz789/s-l500.webp" />
            </div>
            <div class="s-card__title"><span class="su-styled-text primary default">Sony PlayStation 5 Slim</span></div>
            <div class="s-card__subtitle-row">
              <div class="s-card__subtitle">
                <span class="su-styled-text secondary default">Pre-owned</span>
              </div>
            </div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text primary bold large-1 s-card__price">£249.99</span>
            </div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text secondary large">Buy It Now</span>
            </div>
          </li>
        </ul>
        """;

    [Test]
    public void Should_extract_condition_image_and_buying_format_from_a_real_card()
    {
        var summary = new EbaySearchParser().Parse(FullRealEbayCard).Listings.Single();

        Assert.Multiple(() =>
        {
            Assert.That(summary.Condition, Is.EqualTo("Pre-owned"));
            Assert.That(
                summary.PrimaryImageUrl,
                Is.EqualTo("https://i.ebayimg.com/images/g/xyz789/s-l500.webp"));
            Assert.That(summary.BuyingFormat, Is.EqualTo("Buy It Now"));
        });
    }
}
