using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private static readonly string SoldItemDescription = """
        Brand New. Never opened.

        Ps5 Controller Anniversary Edition.
        """.ReplaceLineEndings("\n");

    private static readonly string[] SoldItemImageUrls =
    [
        "https://u-mercari-images.mercdn.net/photos/m44688360101_1.jpg?1789417744",
        "https://u-mercari-images.mercdn.net/photos/m44688360101_2.jpg?1789417744",
    ];

    private static readonly string SoldItemPage = ReadFixture("item-sold-m44688360101.html");

    [Test]
    public void Should_report_the_sold_price_and_sold_date_from_the_sold_item_record()
    {
        var listing = new MercariItemPageParser().Parse(SoldItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("PlayStation 5 DualSense Wireless Controller 30th Anniversary"));
            Assert.That(listing.Status, Is.EqualTo("Sold"));
            Assert.That(listing.Price, Is.EqualTo(140.25m));
            Assert.That(listing.SoldPrice, Is.EqualTo(140.25m));
            Assert.That(listing.SoldDate, Is.EqualTo("2026-09-23T22:21:59Z"));
            Assert.That(listing.Condition, Is.EqualTo("New"));
            Assert.That(listing.Brand, Is.EqualTo("PlayStation"));
            Assert.That(listing.Seller, Is.EqualTo("Tobey Maguire"));
            Assert.That(listing.Description!.ReplaceLineEndings("\n"), Is.EqualTo(SoldItemDescription.ReplaceLineEndings("\n")));
            Assert.That(listing.ImageUrls, Is.EqualTo(SoldItemImageUrls));
            Assert.That(listing.ShippingCost, Is.EqualTo(7.97m));
            Assert.That(listing.OriginalPrice, Is.EqualTo(165.00m));
            Assert.That(listing.PostedUtc, Is.EqualTo(new DateTimeOffset(2026, 9, 14, 20, 29, 4, TimeSpan.Zero)));
            Assert.That(listing.Likes, Is.EqualTo(9));
            Assert.That(listing.BuyingFormat, Is.Null);
        });
    }
}
