using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SearchUsesTheMarketplaceImplementationTests
{
    private const string SearchTerm = "ps5-console";
    private const string EbaySearchUrl = "https://ebay.example/sch";
    private const string MercariSearchUrl = "https://mercari.example/search";

    [Test]
    public async Task Should_use_the_url_builder_and_parser_of_the_requested_marketplace()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        var ebayUrls = BuildUrlService(Marketplace.Ebay, EbaySearchUrl);
        var mercariUrls = BuildUrlService(Marketplace.Mercari, MercariSearchUrl);
        var ebayParser = BuildParser(Marketplace.Ebay, "eBay listing");
        var mercariParser = BuildParser(Marketplace.Mercari, "Mercari listing");
        var search = BuildService(client, [ebayUrls, mercariUrls], [ebayParser, mercariParser]);
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        var runs = new ScrapeRunService(search, store);
        var work = new ScrapeRunWork(RunId: 1, JobId: 2, SearchTerm: SearchTerm, Marketplace.Mercari);

        await runs.Run(work, CancellationToken.None);

        await client.Received(1).GetPageHtml(MercariSearchUrl, Arg.Any<CancellationToken>());
        await store.Received(1).UpsertListings(
            2,
            Arg.Is<IReadOnlyList<ListingSummary>>(listings => listings.Single().Title == "Mercari listing"),
            Arg.Any<CancellationToken>());

        Assert.Multiple(() =>
        {
            mercariUrls.Received(1).BuildSearch(SearchTerm, false, 1);
            mercariParser.Received(1).Parse("<html/>");
            ebayUrls.DidNotReceive().BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>());
            ebayParser.DidNotReceive().Parse(Arg.Any<string>());
        });
    }

    private static ISearchPageService BuildService(
        IScrapeClient client,
        IReadOnlyList<IEbaySearchUrlService> urlServices,
        IReadOnlyList<ISearchPageParser> parsers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(client);

        foreach (var urlService in urlServices)
        {
            services.AddSingleton(urlService);
        }

        foreach (var parser in parsers)
        {
            services.AddSingleton(parser);
        }

        services.AddSingleton(new ScrapeOptions(MaxPages: 1, CollectSold: false));
        services.AddSingleton<ISearchPageService, SearchPageService>();
        return services.BuildServiceProvider().GetRequiredService<ISearchPageService>();
    }

    private static IEbaySearchUrlService BuildUrlService(Marketplace marketplace, string url)
    {
        var service = Substitute.For<IEbaySearchUrlService>();
        service.Marketplace.Returns(marketplace);
        service.SupportsPagination.Returns(true);
        service.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>()).Returns(url);
        return service;
    }

    private static ISearchPageParser BuildParser(Marketplace marketplace, string title)
    {
        var parser = Substitute.For<ISearchPageParser>();
        parser.Marketplace.Returns(marketplace);
        parser.ContainsListingMarkup(Arg.Any<string>()).Returns(true);
        parser.Parse(Arg.Any<string>()).Returns(
            new SearchPageResult(
                [new ListingSummary("123456789012", title, 10m, "GBP", "https://x/itm/1", false, null, null, null)],
                null));
        return parser;
    }
}
