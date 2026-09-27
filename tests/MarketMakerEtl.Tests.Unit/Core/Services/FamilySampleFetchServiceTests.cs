using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilySampleFetchServiceTests
{
    private ISearchPageService _search = null!;
    private IScrapeClient _client = null!;
    private IItemPageParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _search = Substitute.For<ISearchPageService>();
        _client = Substitute.For<IScrapeClient>();
        _parser = Substitute.For<IItemPageParser>();
        _parser.Marketplace.Returns(Marketplace.Mercari);
    }

    [Test]
    public async Task Should_fetch_the_item_detail_description_for_each_sample_listing()
    {
        var listing = BuildListing("m1", "https://mercari.test/item/m1");
        _search.Collect("ps5 controller", Marketplace.Mercari, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([listing], null, []));
        _client.GetPageHtml(listing.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        _parser.Parse("<html/>").Returns(new ItemPageListing(
            "m1", "Title", 25m, "USD", "Good", "Buy It Now", null, null, null, null, null, Description: "Full description text."));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 500));
        var sample = await service.FetchSample("ps5 controller", CancellationToken.None);

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
        var listing = BuildListing("m1", "https://mercari.test/item/m1");
        _search.Collect(Arg.Any<string>(), Arg.Any<Marketplace>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([listing], null, []));
        _client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("boom"));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 500));
        var sample = await service.FetchSample("ps5 controller", CancellationToken.None);

        Assert.That(sample[0].Description, Is.EqualTo("Title"));
    }

    [Test]
    public async Task Should_truncate_the_description_to_the_configured_maximum_length()
    {
        var listing = BuildListing("m1", "https://mercari.test/item/m1");
        _search.Collect(Arg.Any<string>(), Arg.Any<Marketplace>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([listing], null, []));
        _client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        _parser.Parse("<html/>").Returns(new ItemPageListing(
            "m1", "Title", null, null, null, null, null, null, null, null, null, Description: "0123456789"));

        var service = BuildService(BuildOptions(maxSampleListings: 10, maxDescriptionChars: 5));
        var sample = await service.FetchSample("ps5 controller", CancellationToken.None);

        Assert.That(sample[0].Description, Is.EqualTo("01234"));
    }

    [Test]
    public async Task Should_cap_the_sample_at_the_configured_maximum_listing_count()
    {
        var listings = Enumerable.Range(0, 5)
            .Select(i => BuildListing($"m{i}", $"https://mercari.test/item/m{i}"))
            .ToList();
        _search.Collect(Arg.Any<string>(), Arg.Any<Marketplace>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(listings, null, []));
        _client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        _parser.Parse("<html/>").Returns((ItemPageListing?)null);

        var service = BuildService(BuildOptions(maxSampleListings: 2, maxDescriptionChars: 500));
        var sample = await service.FetchSample("ps5 controller", CancellationToken.None);

        Assert.That(sample, Has.Count.EqualTo(2));
    }

    private FamilySampleFetchService BuildService(OnboardingOptions options) =>
        new(_search, _client, new MarketplaceAdapters([], [], [_parser]), options);

    private static OnboardingOptions BuildOptions(int maxSampleListings, int maxDescriptionChars) =>
        new("gpt-6-sol", "medium", 2.0m, 10.0m, maxSampleListings, maxDescriptionChars, 180);

    private static ListingSummary BuildListing(string id, string url) =>
        new(id, "Title", 25m, "USD", url, false, "Good", null, "Buy It Now");
}
