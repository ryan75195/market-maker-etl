using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PrimaryImageReadFromCardImageContainerTests
{
    private const string CardWithImageContainer = """
        <ul class="srp-results">
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=item1">link</a>
            <div class="s-card__image">
              <img alt="Sony PlayStation 5 Slim" src="https://i.ebayimg.com/images/g/abc123/s-l500.webp" />
            </div>
            <div class="s-card__title"><span class="su-styled-text primary default">Sony PlayStation 5 Slim</span></div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text primary bold large-1 s-card__price">£249.99</span>
            </div>
          </li>
        </ul>
        """;

    [Test]
    public void Should_read_primary_image_url_from_the_card_image_container()
    {
        var summaries = new EbaySearchParser().Parse(CardWithImageContainer);

        Assert.That(
            summaries.Single().PrimaryImageUrl,
            Is.EqualTo("https://i.ebayimg.com/images/g/abc123/s-l500.webp"));
    }
}
