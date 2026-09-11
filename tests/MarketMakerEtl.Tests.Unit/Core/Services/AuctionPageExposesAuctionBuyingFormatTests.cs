using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class AuctionPageExposesAuctionBuyingFormatTests
{
    private const string ActiveAuctionPage = """
        <div class=x-item-title>
          <h1 class=x-item-title__mainTitle>Vintage Omega Seamaster 1968</h1>
        </div>
        <div class=x-price-primary>
          <span class=x-price-primary__price>£120.00</span>
        </div>
        <div class=x-bid-price>
          <span>3 bids</span>
        </div>
        """;

    [Test]
    public void Should_report_an_auction_buying_format()
    {
        var listing = EbayItemPageParser.Parse(ActiveAuctionPage);

        Assert.That(listing, Is.Not.Null);
        Assert.That(listing!.BuyingFormat, Is.EqualTo("Auction"));
    }
}
