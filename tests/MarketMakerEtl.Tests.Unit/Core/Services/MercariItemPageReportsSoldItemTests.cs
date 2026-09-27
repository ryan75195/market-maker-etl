using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private static readonly string SoldItemDescription = """
        Hello, up for sale I have PowerA Enhanced Wireless Controller with Lumectra for Nintendo Switch - Black
        Brand new sealed item
        Please see my detailed pictures of the item
        Item is on hand and will be shipped right away
        Free fast shipping from NYC
        Check out my other items
        Thanks for looking!
        """.ReplaceLineEndings("\n");

    private static readonly string[] SoldItemImageUrls =
    [
        "https://u-mercari-images.mercdn.net/photos/m87013616167_1.jpg?1790491149",
        "https://u-mercari-images.mercdn.net/photos/m87013616167_2.jpg?1790491149",
        "https://u-mercari-images.mercdn.net/photos/m87013616167_3.jpg?1790491149",
        "https://u-mercari-images.mercdn.net/photos/m87013616167_4.jpg?1790491149",
    ];

    private static readonly string SoldItemPage = ReadFixture("item-api-sold-m87013616167.json");

    [Test]
    public void Should_report_the_sold_price_and_sold_date_from_the_sold_item_record()
    {
        var listing = new MercariItemPageParser().Parse(SoldItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                listing!.Title,
                Is.EqualTo("PowerA Enhanced Wireless Controller with Lumectra for Nintendo Switch - Black"));
            Assert.That(listing.Status, Is.EqualTo("Sold"));
            Assert.That(listing.Price, Is.EqualTo(32.00m));
            Assert.That(listing.SoldPrice, Is.EqualTo(32.00m));
            Assert.That(listing.SoldDate, Is.EqualTo("2026-09-27T10:17:33Z"));
            Assert.That(listing.Condition, Is.EqualTo("New"));
            Assert.That(listing.Brand, Is.EqualTo("Power A"));
            Assert.That(listing.Seller, Is.EqualTo("Brooklynshop11"));
            Assert.That(listing.Description, Is.EqualTo(SoldItemDescription));
            Assert.That(listing.ImageUrls, Is.EqualTo(SoldItemImageUrls));
            Assert.That(listing.ShippingCost, Is.EqualTo(0m));
            Assert.That(listing.OriginalPrice, Is.EqualTo(40.00m));
            Assert.That(listing.PostedUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1764450562)));
            Assert.That(listing.Likes, Is.EqualTo(9));
            Assert.That(listing.BuyingFormat, Is.Null);
        });
    }
}
