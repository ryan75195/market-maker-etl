using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EbaySearchUrlServiceTests
{
    private static readonly EbaySearchUrlService Service = new();

    [Test]
    public void Should_include_the_search_term_and_page_for_active_listings()
    {
        var url = Service.BuildSearch("playstation 5", sold: false, page: 2);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("_nkw=playstation%205"));
            Assert.That(url, Does.Contain("_pgn=2"));
            Assert.That(url, Does.Not.Contain("LH_Sold=1"));
        });
    }

    [Test]
    public void Should_add_the_sold_filters_when_searching_sold_listings()
    {
        var url = Service.BuildSearch("playstation 5", sold: true, page: 1);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("LH_Sold=1"));
            Assert.That(url, Does.Contain("LH_Complete=1"));
        });
    }

    [Test]
    public void Should_clamp_the_page_to_at_least_one()
    {
        var url = Service.BuildSearch("xbox", sold: false, page: 0);

        Assert.That(url, Does.Contain("_pgn=1"));
    }
}
