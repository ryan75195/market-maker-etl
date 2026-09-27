using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public partial class MercariItemPageParserTests
{
    private const string ActiveItemDescription =
        "This listing is for a Nintendo Switch V1. Bundle comes with everything shown. System is in very good "
        + "condition. Barely played. The Mario controllers are third party. Everything works fine.";

    private static readonly string[] ActiveItemImageUrls =
    [
        "https://u-mercari-images.mercdn.net/photos/m45718142917_1.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_2.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_3.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_4.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_5.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_6.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_7.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_8.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_9.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_10.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_11.jpg?1790465672",
        "https://u-mercari-images.mercdn.net/photos/m45718142917_12.jpg?1790465672",
    ];

    private static readonly string ActiveItemPage = ReadFixture("item-api-active-m45718142917.json");

    [Test]
    public void Should_parse_price_condition_brand_seller_description_photos_shipping_and_posted_date_from_the_active_item_record()
    {
        var listing = new MercariItemPageParser().Parse(ActiveItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("Nintendo switch v1 bundle"));
            Assert.That(listing.Status, Is.EqualTo("Active"));
            Assert.That(listing.Price, Is.EqualTo(150.00m));
            Assert.That(listing.Currency, Is.EqualTo("USD"));
            Assert.That(listing.Condition, Is.EqualTo("Good"));
            Assert.That(listing.Brand, Is.EqualTo("Nintendo"));
            Assert.That(listing.Seller, Is.EqualTo("thehallofpops"));
            Assert.That(listing.Description, Is.EqualTo(ActiveItemDescription));
            Assert.That(listing.ImageUrls, Is.EqualTo(ActiveItemImageUrls));
            Assert.That(listing.PrimaryImageUrl, Is.EqualTo(ActiveItemImageUrls[0]));
            Assert.That(listing.ShippingCost, Is.EqualTo(0m));
            Assert.That(listing.OriginalPrice, Is.EqualTo(165.00m));
            Assert.That(listing.PostedUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1790461236)));
            Assert.That(listing.Likes, Is.EqualTo(5));
            Assert.That(listing.BuyingFormat, Is.Null);
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
        });
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
