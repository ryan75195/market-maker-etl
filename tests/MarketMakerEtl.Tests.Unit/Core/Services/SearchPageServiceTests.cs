using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SearchPageServiceTests
{
    private static readonly ListingSummary Listing =
        new("111111111111", "PS5", 100m, "GBP", "https://x/itm/1", false, null, null, null);

    [Test]
    public async Task Should_collect_listings_from_active_pages()
    {
        var harness = Build(new ScrapeOptions(MaxPages: 1, CollectSold: false));

        var listings = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        Assert.That(listings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_collect_sold_pages_when_enabled()
    {
        var harness = Build(new ScrapeOptions(MaxPages: 1, CollectSold: true));

        await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        await harness.Client.Received(2).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_deduplicate_listings_seen_in_both_directions()
    {
        var harness = Build(new ScrapeOptions(MaxPages: 1, CollectSold: true));

        var listings = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        Assert.That(listings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_stop_paging_when_a_page_has_no_results()
    {
        var harness = Build(new ScrapeOptions(MaxPages: 3, CollectSold: false), empty: true);

        var listings = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);
        await harness.Client.Received(1).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.That(listings, Is.Empty);
    }

    private static Harness Build(ScrapeOptions options, bool empty = false)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");

        var urls = Substitute.For<IEbaySearchUrlService>();
        urls.Marketplace.Returns(Marketplace.Ebay);
        urls.SupportsPagination.Returns(true);
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>()).Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Marketplace.Returns(Marketplace.Ebay);
        parser.ContainsListingMarkup(Arg.Any<string>()).Returns(!empty);
        parser.Parse(Arg.Any<string>()).Returns(
            empty ? new SearchPageResult([], null) : new SearchPageResult([Listing], null));

        return new Harness(
            new SearchPageService(client, [urls], [parser], options, NullLogger<SearchPageService>.Instance),
            client);
    }

    private sealed record Harness(SearchPageService Service, IScrapeClient Client);
}
