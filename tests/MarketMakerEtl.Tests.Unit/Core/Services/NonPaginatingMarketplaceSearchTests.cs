using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class NonPaginatingMarketplaceSearchTests
{
    private const string SearchTerm = "ps5";

    [Test]
    public async Task Should_fetch_a_non_paginating_marketplace_once_whatever_the_page_limit_is()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        var urls = BuildUrlService(Marketplace.Mercari, supportsPagination: false);
        var parser = BuildParser(Marketplace.Mercari);
        var service = new SearchPageService(
            client,
            new MarketplaceAdapters([urls], [parser], []),
            new ScrapeOptions(MaxPages: 5, CollectSold: false),
            new DetailFetchOptions(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3),
            NullLogger<SearchPageService>.Instance);

        var result = await service.Collect(SearchTerm, Marketplace.Mercari, new HashSet<string>(), CancellationToken.None);

        await client.Received(1).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
        urls.Received(1).BuildSearch(SearchTerm, false, 1);

        Assert.That(result.Listings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_fetch_every_configured_page_for_a_paginating_marketplace()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        var urls = BuildUrlService(Marketplace.Ebay, supportsPagination: true);
        var parser = BuildParser(Marketplace.Ebay);
        var service = new SearchPageService(
            client,
            new MarketplaceAdapters([urls], [parser], []),
            new ScrapeOptions(MaxPages: 3, CollectSold: false),
            new DetailFetchOptions(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3),
            NullLogger<SearchPageService>.Instance);

        await service.Collect(SearchTerm, Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);

        await client.Received(3).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IEbaySearchUrlService BuildUrlService(Marketplace marketplace, bool supportsPagination)
    {
        var service = Substitute.For<IEbaySearchUrlService>();
        service.Marketplace.Returns(marketplace);
        service.SupportsPagination.Returns(supportsPagination);
        service.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>()).Returns("https://search");
        return service;
    }

    private static ISearchPageParser BuildParser(Marketplace marketplace)
    {
        var parser = Substitute.For<ISearchPageParser>();
        parser.Marketplace.Returns(marketplace);
        parser.ContainsListingMarkup(Arg.Any<string>()).Returns(true);
        parser.Parse(Arg.Any<string>()).Returns(
            new SearchPageResult(
                [new ListingSummary("111111111111", "PS5", 10m, "GBP", "https://x/itm/1", false, null, null, null)],
                null));
        return parser;
    }
}
