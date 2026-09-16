using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public partial class MercariItemPageParserTests
{
    private const string ActiveItemPage = """
        <html>
        <body>
        <div data-testid="ItemDetails">
          <h1 data-testid="ItemName">Nintendo Switch OLED Console</h1>
          <div data-testid="ItemPrice">$289.00</div>
          <div data-testid="ItemCondition">Used - Good</div>
          <div data-testid="ItemBrand">Nintendo</div>
          <div data-testid="ItemSeller">Retro Games Shop</div>
          <div data-testid="ItemGallery">
            <img data-testid="ItemImage" src="https://static.mercdn.net/m92390261760.jpg" alt="Nintendo Switch OLED Console" />
          </div>
        </div>
        </body>
        </html>
        """;

    [Test]
    public void Should_parse_title_price_condition_brand_seller_and_image_from_active_item_markup()
    {
        var listing = new MercariItemPageParser().Parse(ActiveItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("Nintendo Switch OLED Console"));
            Assert.That(listing.Price, Is.EqualTo(289.00m));
            Assert.That(listing.Condition, Is.EqualTo("Used - Good"));
            Assert.That(listing.Brand, Is.EqualTo("Nintendo"));
            Assert.That(listing.Seller, Is.EqualTo("Retro Games Shop"));
            Assert.That(listing.PrimaryImageUrl, Is.EqualTo("https://static.mercdn.net/m92390261760.jpg"));
        });
    }
}
