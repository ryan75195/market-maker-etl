using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldPageExposesSoldStatusPriceAndDateTests
{
    private const string SoldPage = """
        <div class=x-item-title>
          <h1 class=x-item-title__mainTitle>Sony WH-1000XM5 Headphones</h1>
        </div>
        <div class=x-photos-cvip>
          <span class=ux-textspans>SOLD</span>
        </div>
        <div class=x-item-condensed-card__sold-price>£180.50</div>
        <div class=d-top-panel-message>This listing sold on Fri, 11 Sep at 6:09 PM.</div>
        """;

    [Test]
    public void Should_report_sold_status_the_sold_price_and_the_sold_date()
    {
        var listing = EbayItemPageParser.Parse(SoldPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Status, Is.EqualTo("Sold"));
            Assert.That(listing.SoldPrice, Is.EqualTo(180.50m));
            Assert.That(listing.SoldDate, Is.EqualTo("Fri, 11 Sep at 6:09 PM"));
        });
    }
}
