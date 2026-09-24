using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public partial class MercariItemPageParserTests
{
    private static readonly string ActiveItemDescription = """
        Hi!

        I have a PlayStation 5 Digital Console with 1 TB of storage.

        The console is in good shape cosmetically. It has the typical scuffs seen in gently used consoles.

        The console functions great and works 100%.

        This is a digirtal console to take advantage of Sony's online library. No disc drive.

        Comes with console and power cord and controller.

        Sony brand controller feels good, no drift, springy triggers, smooth button presses.

        Summary:
        One perfectly working PlayStation 5 Digital console with a power cord and controller.

        If you have any questions, please don't hesitate to ask!
        """.ReplaceLineEndings("\n");

    private static readonly string[] ActiveItemImageUrls =
    [
        "https://u-mercari-images.mercdn.net/photos/m71344610988_1.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_2.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_3.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_4.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_5.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_6.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_7.jpg?1790200178",
        "https://u-mercari-images.mercdn.net/photos/m71344610988_8.jpg?1790200178",
    ];

    private static readonly string ActiveItemPage = ReadFixture("item-active-m71344610988.html");

    [Test]
    public void Should_parse_price_condition_brand_seller_description_photos_shipping_and_posted_date_from_the_active_item_record()
    {
        var listing = new MercariItemPageParser().Parse(ActiveItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("Sony PlayStation 5 PS5 Digital Console with Controller and Power Cable"));
            Assert.That(listing.Status, Is.EqualTo("Active"));
            Assert.That(listing.Price, Is.EqualTo(389.00m));
            Assert.That(listing.Currency, Is.EqualTo("USD"));
            Assert.That(listing.Condition, Is.EqualTo("Good"));
            Assert.That(listing.Brand, Is.EqualTo("PlayStation"));
            Assert.That(listing.Seller, Is.EqualTo("Nerd Mom Electronics"));
            Assert.That(listing.Description!.ReplaceLineEndings("\n"), Is.EqualTo(ActiveItemDescription.ReplaceLineEndings("\n")));
            Assert.That(listing.ImageUrls, Is.EqualTo(ActiveItemImageUrls));
            Assert.That(listing.PrimaryImageUrl, Is.EqualTo(ActiveItemImageUrls[0]));
            Assert.That(listing.ShippingCost, Is.EqualTo(0m));
            Assert.That(listing.OriginalPrice, Is.EqualTo(399.00m));
            Assert.That(listing.PostedUtc, Is.EqualTo(new DateTimeOffset(2026, 9, 22, 19, 24, 20, TimeSpan.Zero)));
            Assert.That(listing.Likes, Is.EqualTo(32));
            Assert.That(listing.BuyingFormat, Is.Null);
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
        });
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
