using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilySampleFetchServiceTests
{
    private const string SearchTerm = "ps5 controller";
    private const string ActiveOffset0Url = "https://mercari.test/search?sold=false&offset=0";
    private const string ActiveOffset100Url = "https://mercari.test/search?sold=false&offset=100";
    private const string SoldOffset0Url = "https://mercari.test/search?sold=true&offset=0";
    private const string SoldOffset100Url = "https://mercari.test/search?sold=true&offset=100";

    private IScrapeClient _client = null!;
    private IEbaySearchUrlService _urls = null!;
    private IPriceBandSearchUrlService _bandUrls = null!;
    private ISearchPageParser _searchParser = null!;
    private IItemPageParser _itemParser = null!;

    [SetUp]
    public void SetUp()
    {
        _client = Substitute.For<IScrapeClient>();
        var urls = Substitute.For<IEbaySearchUrlService, IPriceBandSearchUrlService>();
        urls.Marketplace.Returns(Marketplace.Mercari);
        _urls = urls;
        _bandUrls = (IPriceBandSearchUrlService)urls;
        _bandUrls.BuildSearch(SearchTerm, false, null, null, 0).Returns(ActiveOffset0Url);
        _bandUrls.BuildSearch(SearchTerm, false, null, null, 100).Returns(ActiveOffset100Url);
        _bandUrls.BuildSearch(SearchTerm, true, null, null, 0).Returns(SoldOffset0Url);
        _bandUrls.BuildSearch(SearchTerm, true, null, null, 100).Returns(SoldOffset100Url);

        _searchParser = Substitute.For<ISearchPageParser>();
        _searchParser.Marketplace.Returns(Marketplace.Mercari);
        StubSearchPage(ActiveOffset0Url, []);
        StubSearchPage(ActiveOffset100Url, []);
        StubSearchPage(SoldOffset0Url, []);
        StubSearchPage(SoldOffset100Url, []);

        _itemParser = Substitute.For<IItemPageParser>();
        _itemParser.Marketplace.Returns(Marketplace.Mercari);
    }

    [Test]
    public async Task Should_fetch_the_item_detail_description_for_each_sample_listing()
    {
        var listing = BuildListing("m1", "https://mercari.test/item/m1", price: 25m, sold: false);
        StubSearchPage(ActiveOffset0Url, [listing]);
        _client.GetPageHtml(listing.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        _itemParser.Parse("<html/>").Returns(new ItemPageListing(
            "m1", "Title", 25m, "USD", "Good", "Buy It Now", null, null, null, null, null, Description: "Full description text."));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 500));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.That(sample, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(sample[0].Id, Is.EqualTo("m1"));
            Assert.That(sample[0].Description, Is.EqualTo("Full description text."));
            Assert.That(sample[0].Sold, Is.False);
            Assert.That(sample[0].Price, Is.EqualTo(25m));
        });
    }

    [Test]
    public async Task Should_fall_back_to_the_listing_title_when_the_detail_fetch_fails()
    {
        var listing = BuildListing("m1", "https://mercari.test/item/m1", price: 25m, sold: false);
        StubSearchPage(ActiveOffset0Url, [listing]);
        _client.GetPageHtml(listing.Url!, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("boom"));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 500));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.That(sample[0].Description, Is.EqualTo("Title"));
    }

    [Test]
    public async Task Should_truncate_the_description_to_the_configured_maximum_length()
    {
        var listing = BuildListing("m1", "https://mercari.test/item/m1", price: 25m, sold: false);
        StubSearchPage(ActiveOffset0Url, [listing]);
        _client.GetPageHtml(listing.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        _itemParser.Parse("<html/>").Returns(new ItemPageListing(
            "m1", "Title", null, null, null, null, null, null, null, null, null, Description: "0123456789"));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 5));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.That(sample[0].Description, Is.EqualTo("01234"));
    }

    [Test]
    public async Task Should_cap_the_sample_at_the_configured_maximum_listing_count()
    {
        var active = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"a{i}", $"https://mercari.test/item/a{i}", price: 10m + i, sold: false))
            .ToList();
        var sold = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"s{i}", $"https://mercari.test/item/s{i}", price: 10m + i, sold: true))
            .ToList();
        StubSearchPage(ActiveOffset0Url, active);
        StubSearchPage(SoldOffset0Url, sold);
        _client.GetPageHtml(Arg.Is<string>(u => u.StartsWith("https://mercari.test/item/", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns("<html/>");

        var service = BuildService(BuildOptions(maxSampleListings: 2, maxDescriptionChars: 500));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.That(sample, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Should_include_both_sold_and_active_listings_in_the_sample()
    {
        var active = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"a{i}", $"https://mercari.test/item/a{i}", price: 10m + i, sold: false))
            .ToList();
        var sold = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"s{i}", $"https://mercari.test/item/s{i}", price: 20m + i, sold: true))
            .ToList();
        StubSearchPage(ActiveOffset0Url, active);
        StubSearchPage(SoldOffset0Url, sold);
        _client.GetPageHtml(Arg.Is<string>(u => u.StartsWith("https://mercari.test/item/", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns("<html/>");

        var service = BuildService(BuildOptions(maxSampleListings: 6, maxDescriptionChars: 500));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sample, Has.Some.Matches<FamilySampleListing>(l => l.Sold));
            Assert.That(sample, Has.Some.Matches<FamilySampleListing>(l => !l.Sold));
        });
    }

    [Test]
    public async Task Should_include_upper_price_range_listings_when_the_cheapest_results_are_noise()
    {
        var cheapAccessories = Enumerable.Range(0, 60)
            .Select(i => BuildListing($"cheap{i}", $"https://mercari.test/item/cheap{i}", price: 1m + i, sold: false))
            .ToList();
        var theActualProduct = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"real{i}", $"https://mercari.test/item/real{i}", price: 500m + i, sold: false))
            .ToList();
        StubSearchPage(ActiveOffset0Url, cheapAccessories.Take(30).ToList());
        StubSearchPage(ActiveOffset100Url, cheapAccessories.Skip(30).Concat(theActualProduct).ToList());
        _client.GetPageHtml(Arg.Is<string>(u => u.StartsWith("https://mercari.test/item/", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns("<html/>");

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 500));
        var sample = await service.FetchSample(SearchTerm, CancellationToken.None);

        Assert.That(sample, Has.Some.Matches<FamilySampleListing>(l => l.Price >= 500m));
    }

    [Test]
    public async Task Should_bound_the_number_of_search_requests_regardless_of_result_volume()
    {
        var manyActive = Enumerable.Range(0, 200)
            .Select(i => BuildListing($"a{i}", $"https://mercari.test/item/a{i}", price: 1m + i, sold: false))
            .ToList();
        StubSearchPage(ActiveOffset0Url, manyActive.Take(100).ToList());
        StubSearchPage(ActiveOffset100Url, manyActive.Skip(100).ToList());
        _client.GetPageHtml(Arg.Is<string>(u => u.StartsWith("https://mercari.test/item/", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns("<html/>");

        var service = BuildService(BuildOptions(maxSampleListings: 20, maxDescriptionChars: 500));
        await service.FetchSample(SearchTerm, CancellationToken.None);

        await _client.Received(1).GetPageHtml(ActiveOffset0Url, Arg.Any<CancellationToken>());
        await _client.Received(1).GetPageHtml(ActiveOffset100Url, Arg.Any<CancellationToken>());
        await _client.Received(1).GetPageHtml(SoldOffset0Url, Arg.Any<CancellationToken>());
        await _client.Received(1).GetPageHtml(SoldOffset100Url, Arg.Any<CancellationToken>());
    }

    private void StubSearchPage(string url, IReadOnlyList<ListingSummary> listings)
    {
        var html = $"html:{url}";
        _client.GetPageHtml(url, Arg.Any<CancellationToken>()).Returns(html);
        _searchParser.Parse(html).Returns(new SearchPageResult(listings, listings.Count));
    }

    private FamilySampleFetchService BuildService(OnboardingOptions options) =>
        new(_client, new MarketplaceAdapters([_urls], [_searchParser], [_itemParser]), options);

    private static OnboardingOptions BuildOptions(int maxSampleListings, int maxDescriptionChars) =>
        new("gpt-6-sol", "medium", maxSampleListings, maxDescriptionChars, 180);

    private static ListingSummary BuildListing(string id, string url, decimal price, bool sold) =>
        new(id, "Title", price, "USD", url, sold, "Good", null, "Buy It Now");
}
