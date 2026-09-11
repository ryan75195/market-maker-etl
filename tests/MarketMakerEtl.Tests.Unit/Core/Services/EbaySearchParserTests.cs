using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EbaySearchParserTests
{
    private const string TwoItemPage = """
        <ul>
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=x">link</a>
            <span class="s-card__title">PlayStation 5 Slim</span>
            <span class="s-card__price">£249.99</span>
          </li>
          <li class="s-card" data-viewport="2">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/987654321098">link</a>
            <span class="s-card__title">PlayStation 5 Digital</span>
            <span class="s-card__price">£199.50</span>
            <span class="s-item__title--tagblock">Sold 12 Nov 2025</span>
          </li>
        </ul>
        """;

    [Test]
    public void Should_parse_each_listing_card_into_a_summary()
    {
        var parser = new EbaySearchParser();

        var summaries = parser.Parse(TwoItemPage);

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(2));
            Assert.That(summaries[0].ListingId, Is.EqualTo("123456789012"));
            Assert.That(summaries[0].Title, Is.EqualTo("PlayStation 5 Slim"));
            Assert.That(summaries[0].Price, Is.EqualTo(249.99m));
            Assert.That(summaries[0].Currency, Is.EqualTo("£"));
            Assert.That(summaries[0].Url, Is.EqualTo("https://www.ebay.co.uk/itm/123456789012"));
            Assert.That(summaries[0].IsSold, Is.False);
        });
    }

    [Test]
    public void Should_mark_a_listing_with_a_sold_tag_as_sold()
    {
        var parser = new EbaySearchParser();

        var summaries = parser.Parse(TwoItemPage);

        Assert.That(summaries[1].IsSold, Is.True);
    }

    [Test]
    public void Should_ignore_items_without_a_numeric_listing_id()
    {
        const string page = """
            <ul>
              <li class="s-card" data-viewport="1">
                <a class="s-card__link" href="https://www.ebay.co.uk/itm/not-an-id">link</a>
              </li>
            </ul>
            """;

        var summaries = new EbaySearchParser().Parse(page);

        Assert.That(summaries, Is.Empty);
    }
}
