using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchUrlServiceTests
{
    private static readonly MercariSearchUrlService Service = new();

    [Test]
    public void Should_target_the_mercari_search_page_with_the_search_term()
    {
        var url = Service.BuildSearch("playstation 5", sold: false, page: 1);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://www.mercari.com/search/?"));
            Assert.That(url, Does.Contain("keyword=playstation%205"));
            Assert.That(url, Does.Not.Contain("itemStatuses=2"));
        });
    }

    [Test]
    public void Should_restrict_active_searches_to_on_sale_items()
    {
        var url = Service.BuildSearch("playstation 5", sold: false, page: 1);

        Assert.That(url, Does.Contain("itemStatuses=1"));
    }

    [Test]
    public void Should_restrict_to_sold_items_when_sold_listings_are_requested()
    {
        var url = Service.BuildSearch("playstation 5", sold: true, page: 1);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://www.mercari.com/search/?"));
            Assert.That(url, Does.Contain("keyword=playstation%205"));
            Assert.That(url, Does.Contain("itemStatuses=2"));
        });
    }

    [Test]
    public void Should_narrow_the_search_by_brand_category_condition_and_price_range()
    {
        var request = new MercariSearchRequest(
            SearchTerm: "playstation 5",
            Sold: false,
            BrandId: "4242",
            CategoryId: "9999",
            Condition: "used",
            MinPrice: 100m,
            MaxPrice: 500m);

        var url = Service.BuildSearch(request);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("keyword=playstation%205"));
            Assert.That(url, Does.Contain("brandIds=4242"));
            Assert.That(url, Does.Contain("categoryIds=9999"));
            Assert.That(url, Does.Contain("itemConditions=used"));
            Assert.That(url, Does.Contain("minPrice=10000"));
            Assert.That(url, Does.Contain("maxPrice=50000"));
        });
    }

    [Test]
    public void Should_send_price_filters_in_cents()
    {
        var request = new MercariSearchRequest(
            SearchTerm: "playstation 5",
            Sold: false,
            MinPrice: 1.00m,
            MaxPrice: 2.00m);

        var url = Service.BuildSearch(request);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("minPrice=100"));
            Assert.That(url, Does.Contain("maxPrice=200"));
        });
    }

    [Test]
    public void Should_build_a_banded_search_url_with_cents_price_filters()
    {
        IPriceBandSearchUrlService bandService = Service;

        var url = bandService.BuildSearch("playstation 5", sold: true, minPrice: 1.00m, maxPrice: 2.00m);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://www.mercari.com/search/?"));
            Assert.That(url, Does.Contain("keyword=playstation%205"));
            Assert.That(url, Does.Contain("itemStatuses=2"));
            Assert.That(url, Does.Contain("minPrice=100"));
            Assert.That(url, Does.Contain("maxPrice=200"));
        });
    }

    [Test]
    public void Should_build_a_banded_active_search_url_restricted_to_on_sale_items()
    {
        IPriceBandSearchUrlService bandService = Service;

        var url = bandService.BuildSearch("playstation 5", sold: false, minPrice: 1.00m, maxPrice: 2.00m);

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://www.mercari.com/search/?"));
            Assert.That(url, Does.Contain("keyword=playstation%205"));
            Assert.That(url, Does.Contain("itemStatuses=1"));
            Assert.That(url, Does.Contain("minPrice=100"));
            Assert.That(url, Does.Contain("maxPrice=200"));
        });
    }
}
