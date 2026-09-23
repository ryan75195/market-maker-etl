using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private const string SoldPageWithoutItemRecord = """
        <html>
        <body>
        <h1 data-testid="ItemName">Nintendo Switch OLED Console</h1>
        <p data-testid="ItemPrice">$289.00</p>
        <button data-testid="SoldListing">Item sold</button>
        <span data-testid="ItemDetailsCondition"><p data-testid="ItemDetailsCondition">Good</p></span>
        <span data-testid="ItemDetailsBrand"><a href="/us/brand/nintendo-1/"><p>Nintendo</p></a></span>
        <p data-testid="ItemDetailsSellerName">Retro Games Shop</p>
        <div data-testid="ItemDetailsShipping"><span>Free</span></div>
        <p data-testid="ItemDetailsDescription">Barely used, comes with dock and two Joy-Cons.</p>
        <span data-testid="ItemDetailsPosted">09/01/26</span>
        </body>
        </html>
        """;

    [Test]
    public void Should_fall_back_to_the_real_data_testid_hooks_when_no_item_record_is_present()
    {
        var listing = new MercariItemPageParser().Parse(SoldPageWithoutItemRecord);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Title, Is.EqualTo("Nintendo Switch OLED Console"));
            Assert.That(listing.Price, Is.EqualTo(289.00m));
            Assert.That(listing.Status, Is.EqualTo("Sold"));
            Assert.That(listing.Condition, Is.EqualTo("Good"));
            Assert.That(listing.Brand, Is.EqualTo("Nintendo"));
            Assert.That(listing.Seller, Is.EqualTo("Retro Games Shop"));
            Assert.That(listing.ShippingCost, Is.EqualTo(0m));
            Assert.That(listing.Description, Is.EqualTo("Barely used, comes with dock and two Joy-Cons."));
            Assert.That(listing.PostedUtc, Is.EqualTo(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        });
    }
}
