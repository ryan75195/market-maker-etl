using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private const string SoldItemPage = """
        <html>
        <body>
        <div data-testid="ItemDetails">
          <h1 data-testid="ItemName">Sony PlayStation 5 Console</h1>
          <div data-testid="ItemPrice">$429.00</div>
          <div data-testid="ItemCondition">Used - Good</div>
          <div data-testid="ItemBrand">Sony</div>
          <div data-testid="ItemSeller">GameTrader</div>
          <div data-testid="ItemGallery">
            <img data-testid="ItemImage" src="https://static.mercdn.net/m92390261761.jpg" alt="Sony PlayStation 5 Console" />
          </div>
          <div data-testid="ItemSoldBanner">Item sold</div>
          <div data-testid="ItemSoldBadge">SOLD 4m ago</div>
        </div>
        </body>
        </html>
        """;

    [Test]
    public void Should_report_a_sold_item_from_sold_item_markup()
    {
        var listing = new MercariItemPageParser().Parse(SoldItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Status, Is.EqualTo("Sold"));
            Assert.That(listing.Title, Is.EqualTo("Sony PlayStation 5 Console"));
        });
    }
}
