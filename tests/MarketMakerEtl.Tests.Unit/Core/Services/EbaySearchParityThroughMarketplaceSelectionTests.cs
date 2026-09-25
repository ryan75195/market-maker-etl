using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EbaySearchParityThroughMarketplaceSelectionTests
{
    private const string SearchTerm = "ps5-console";

    private const string KnownEbayPage = """
        <ul>
          <li class="s-card" data-viewport="true">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?campid=1">Sony PlayStation 5 Console</a>
            <div class="s-card__title">Sony PlayStation 5 Console</div>
            <div class="s-card__price">£250.00</div>
          </li>
        </ul>
        """;

    [Test]
    public async Task Should_request_the_same_ebay_url_and_parse_the_same_listings_as_the_ebay_implementation()
    {
        var expectedUrl = new EbaySearchUrlService().BuildSearch(SearchTerm, sold: false, page: 1);
        var expected = new EbaySearchParser().Parse(KnownEbayPage).Listings;
        var client = new CapturingScrapeClient(KnownEbayPage);
        var service = new SearchPageService(
            client,
            new MarketplaceAdapters([new EbaySearchUrlService()], [new EbaySearchParser()], []),
            new ScrapeOptions(MaxPages: 1, CollectSold: false),
            TimeProvider.System,
            NullLogger<SearchPageService>.Instance);

        var result = await service.Collect(SearchTerm, Marketplace.Ebay, new HashSet<string>(), CancellationToken.None);
        var listings = result.Listings;

        Assert.Multiple(() =>
        {
            Assert.That(client.RequestedUrls, Is.EqualTo(new[] { expectedUrl }));
            Assert.That(listings.Select(l => l.ListingId), Is.EqualTo(expected.Select(l => l.ListingId)));
            Assert.That(listings.Select(l => l.Title), Is.EqualTo(expected.Select(l => l.Title)));
            Assert.That(listings.Select(l => l.Price), Is.EqualTo(expected.Select(l => l.Price)));
        });
    }

    private sealed class CapturingScrapeClient(string html) : IScrapeClient
    {
        private readonly List<string> _requestedUrls = [];

        public IReadOnlyList<string> RequestedUrls => _requestedUrls;

        public Task<string> GetPageHtml(string url, CancellationToken ct)
        {
            _requestedUrls.Add(url);
            return Task.FromResult(html);
        }
    }
}
