using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private const string StatusJsonProperty = "\"status\": \"on_sale\",";

    [TestCase("on_sale", "Active")]
    [TestCase("trading", "Sold")]
    [TestCase("sold_out", "Sold")]
    [TestCase("cancelled", "Ended")]
    [TestCase("expired", "Ended")]
    public void Should_map_the_item_detail_state_to_a_listing_status(string state, string expectedStatus)
    {
        var html = ActiveItemPage.Replace(StatusJsonProperty, $"\"status\": \"{state}\",", StringComparison.Ordinal);

        var listing = new MercariItemPageParser().Parse(html);

        Assert.That(listing, Is.Not.Null);
        Assert.That(listing!.Status, Is.EqualTo(expectedStatus));
    }

    [Test]
    public void Should_not_populate_sold_price_or_sold_date_for_an_ended_listing()
    {
        var html = ActiveItemPage.Replace(StatusJsonProperty, "\"status\": \"cancelled\",", StringComparison.Ordinal);

        var listing = new MercariItemPageParser().Parse(html);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Status, Is.EqualTo("Ended"));
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
        });
    }

    [Test]
    public void Should_leave_the_listing_status_null_when_the_item_record_has_no_status()
    {
        var html = ActiveItemPage.Replace(StatusJsonProperty, string.Empty, StringComparison.Ordinal);

        var listing = new MercariItemPageParser().Parse(html);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.Status, Is.Null);
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
        });
    }
}
