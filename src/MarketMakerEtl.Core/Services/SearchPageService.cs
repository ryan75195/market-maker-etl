using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class SearchPageService : ISearchPageService
{
    private readonly IScrapeClient _client;
    private readonly IEnumerable<IEbaySearchUrlService> _urlServices;
    private readonly IEnumerable<ISearchPageParser> _parsers;
    private readonly ScrapeOptions _options;
    private readonly ILogger<SearchPageService> _logger;

    public SearchPageService(
        IScrapeClient client,
        IEnumerable<IEbaySearchUrlService> urlServices,
        IEnumerable<ISearchPageParser> parsers,
        ScrapeOptions options,
        ILogger<SearchPageService> logger)
    {
        _client = client;
        _urlServices = urlServices;
        _parsers = parsers;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ListingSummary>> Collect(
        string searchTerm,
        Marketplace marketplace,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, ListingSummary>(StringComparer.Ordinal);
        var urls = SelectUrlService(marketplace);
        var parser = SelectParser(marketplace);

        await CollectDirection(searchTerm, sold: false, urls, parser, merged, knownSoldListingIds, ct);

        if (_options.CollectSold)
        {
            await CollectDirection(searchTerm, sold: true, urls, parser, merged, knownSoldListingIds, ct);
        }

        return merged.Values.ToList();
    }

    private IEbaySearchUrlService SelectUrlService(Marketplace marketplace) =>
        _urlServices.SingleOrDefault(service => service.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search URL implementation registered for marketplace {marketplace}.");

    private ISearchPageParser SelectParser(Marketplace marketplace) =>
        _parsers.SingleOrDefault(parser => parser.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search parser registered for marketplace {marketplace}.");

    private async Task CollectDirection(
        string searchTerm,
        bool sold,
        IEbaySearchUrlService urls,
        ISearchPageParser parser,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        if (urls is IPriceBandSearchUrlService bandUrls)
        {
            var collector = new MercariPriceBandCollector(
                _client, bandUrls, parser, _options.MaxBandsPerDirection, _logger);
            await collector.Collect(searchTerm, sold, merged, knownSoldListingIds, ct);
            return;
        }

        var pageLimit = urls.SupportsPagination ? _options.MaxPages : 1;

        for (var page = 1; page <= pageLimit; page++)
        {
            var url = urls.BuildSearch(searchTerm, sold, page);
            var html = await _client.GetPageHtml(url, ct);
            var pageResult = parser.Parse(html);

            if (pageResult.Listings.Count == 0)
            {
                ThrowIfListingMarkupProducedNoResults(parser, html);
                return;
            }

            foreach (var listing in pageResult.Listings)
            {
                merged[listing.ListingId] = listing;
            }
        }
    }

    private static void ThrowIfListingMarkupProducedNoResults(ISearchPageParser parser, string html)
    {
        if (!parser.ContainsListingMarkup(html))
        {
            return;
        }

        throw new InvalidOperationException(
            "Search page contained listing markup but produced no parsed listings.");
    }
}
