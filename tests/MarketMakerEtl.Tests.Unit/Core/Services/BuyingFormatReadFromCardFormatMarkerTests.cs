using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class BuyingFormatReadFromCardFormatMarkerTests
{
    private const string CardShowingBuyItNow = """
        <ul class="srp-results">
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=item1">link</a>
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

    private const string CardShowingAuction = """
        <ul class="srp-results">
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/987654321098?hash=item2">link</a>
            <div class="s-card__title"><span class="su-styled-text primary default">Sony PlayStation 5 Digital</span></div>
            <div class="s-card__subtitle-row">
              <div class="s-card__subtitle">
                <span class="su-styled-text secondary default">Used</span>
              </div>
            </div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text primary bold large-1 s-card__price">£199.50</span>
            </div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text secondary large">Auction</span>
            </div>
          </li>
        </ul>
        """;

    [Test]
    public void Should_read_buying_format_when_the_card_shows_a_buy_it_now_marker()
    {
        var summaries = new EbaySearchParser().Parse(CardShowingBuyItNow);

        Assert.That(summaries.Single().BuyingFormat, Is.EqualTo("Buy It Now"));
    }

    [Test]
    public void Should_read_buying_format_when_the_card_shows_an_auction_marker()
    {
        var summaries = new EbaySearchParser().Parse(CardShowingAuction);

        Assert.That(summaries.Single().BuyingFormat, Is.EqualTo("Auction"));
    }
}
