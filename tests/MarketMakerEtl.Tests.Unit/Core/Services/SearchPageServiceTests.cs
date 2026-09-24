using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
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

        var result = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        Assert.That(result.Listings, Has.Count.EqualTo(1));
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

        var result = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        Assert.That(result.Listings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_stop_paging_when_a_page_has_no_results()
    {
        var harness = Build(new ScrapeOptions(MaxPages: 3, CollectSold: false), empty: true);

        var result = await harness.Service.Collect("ps5", Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);
        await harness.Client.Received(1).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.That(result.Listings, Is.Empty);
    }

    [Test]
    public async Task Should_record_an_issue_when_a_price_band_search_yields_no_results_and_no_total()
    {
        var harness = BuildMercari(new ScrapeOptions(MaxPages: 1, CollectSold: false, MaxBandsPerDirection: 8), totalReported: null);

        var result = await harness.Service.Collect("pokemon card lot", Marketplace.Mercari, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Listings, Is.Empty);
            Assert.That(result.Issues, Has.Some.Matches<ScrapeRunIssueDetails>(
                issue => issue.IssueType == "SearchYieldedNoResults"));
        });
    }

    [Test]
    public async Task Should_not_record_a_no_results_issue_when_a_real_total_is_reported()
    {
        var harness = BuildMercari(new ScrapeOptions(MaxPages: 1, CollectSold: false, MaxBandsPerDirection: 8), totalReported: 0);

        var result = await harness.Service.Collect("pokemon card lot", Marketplace.Mercari, new HashSet<string>(), CancellationToken.None);

        Assert.That(result.Issues, Has.None.Matches<ScrapeRunIssueDetails>(
            issue => issue.IssueType == "SearchYieldedNoResults"));
    }

    [Test]
    public async Task Should_record_a_sold_backfill_window_issue_when_a_jobs_first_sold_run_backfills()
    {
        var harness = BuildMercari(
            new ScrapeOptions(MaxPages: 1, CollectSold: true, MaxBandsPerDirection: 9, SoldBackfillDays: 30),
            totalReported: 0);

        var result = await harness.Service.Collect("pokemon card lot", Marketplace.Mercari, new HashSet<string>(), CancellationToken.None);

        Assert.That(result.Issues, Has.Some.Matches<ScrapeRunIssueDetails>(
            issue => issue.IssueType == "SoldBackfillWindow"));
    }

    [Test]
    public async Task Should_not_record_a_backfill_window_issue_on_a_later_incremental_run()
    {
        var harness = BuildMercari(
            new ScrapeOptions(MaxPages: 1, CollectSold: true, MaxBandsPerDirection: 9, SoldBackfillDays: 30),
            totalReported: 0);

        var result = await harness.Service.Collect(
            "pokemon card lot", Marketplace.Mercari, new HashSet<string> { "already-known" }, CancellationToken.None);

        Assert.That(result.Issues, Has.None.Matches<ScrapeRunIssueDetails>(
            issue => issue.IssueType == "SoldBackfillWindow"));
    }

    private static Harness BuildMercari(ScrapeOptions options, int? totalReported)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("{}");

        var urls = Substitute.For<IEbaySearchUrlService, IPriceBandSearchUrlService>();
        urls.Marketplace.Returns(Marketplace.Mercari);
        ((IPriceBandSearchUrlService)urls)
            .BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Marketplace.Returns(Marketplace.Mercari);
        parser.Parse(Arg.Any<string>()).Returns(new SearchPageResult([], totalReported));

        var itemParser = Substitute.For<IItemPageParser>();
        itemParser.Marketplace.Returns(Marketplace.Mercari);

        var adapters = new MarketplaceAdapters([(IEbaySearchUrlService)urls], [parser], [itemParser]);
        return new Harness(
            new SearchPageService(client, adapters, options, TimeProvider.System, NullLogger<SearchPageService>.Instance),
            client);
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

        var adapters = new MarketplaceAdapters([urls], [parser], []);
        return new Harness(
            new SearchPageService(client, adapters, options, TimeProvider.System, NullLogger<SearchPageService>.Instance),
            client);
    }

    private sealed record Harness(SearchPageService Service, IScrapeClient Client);
}
