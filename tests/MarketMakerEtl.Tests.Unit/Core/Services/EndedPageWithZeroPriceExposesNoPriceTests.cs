using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EndedPageWithZeroPriceExposesNoPriceTests
{
    private const string EndedPageWithZeroPrice = """
        <div class=x-item-title>
          <h1 class=x-item-title__mainTitle>Nintendo Switch OLED Console</h1>
        </div>
        <div class=x-photos-cvip>
          <span class=ux-textspans>ENDED</span>
        </div>
        <div class=x-price-primary>
          <span class=x-price-primary__price>£0.00</span>
        </div>
        """;

    [Test]
    public void Should_report_no_price_when_an_ended_page_displays_zero()
    {
        var listing = EbayItemPageParser.Parse(EndedPageWithZeroPrice);

        Assert.That(listing, Is.Not.Null);
        Assert.That(listing!.Price, Is.Null);
    }
}
