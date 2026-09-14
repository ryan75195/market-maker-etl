using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EbayItemPageParserServiceTests
{
    private const string ActiveItemPage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Canon EOS R6 Camera Body</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£1,299.00</span>
        </div>
        """;

    [Test]
    public void Should_parse_an_ebay_item_page_into_the_same_listing_as_the_ebay_parser()
    {
        var expected = EbayItemPageParser.Parse(ActiveItemPage);
        var parsed = new EbayItemPageParserService().Parse(ActiveItemPage);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Title, Is.EqualTo(expected!.Title));
            Assert.That(parsed.Price, Is.EqualTo(expected.Price));
            Assert.That(parsed.Currency, Is.EqualTo(expected.Currency));
            Assert.That(parsed.Status, Is.EqualTo(expected.Status));
        });
    }
}
