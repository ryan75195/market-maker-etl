using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    [Test]
    public void Should_leave_buying_format_and_sold_date_unset_for_a_mercari_item()
    {
        var listing = new MercariItemPageParser().Parse(SoldItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.BuyingFormat, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
        });
    }
}
