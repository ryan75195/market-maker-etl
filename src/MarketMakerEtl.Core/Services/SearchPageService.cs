using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class SearchPageService : ISearchPageService
{
    private readonly IScrapeClient _client;
    private readonly IEnumerable<IEbaySearchUrlService> _urlServices;
    private readonly IEnumerable<ISearchPageParser> _parsers;
    private readonly ScrapeOptions _options;

    public SearchPageService(
        IScrapeClient client,
        IEnumerable<IEbaySearchUrlService> urlServices,
        IEnumerable<ISearchPageParser> parsers,
        ScrapeOptions options)
    {
        _client = client;
        _urlServices = urlServices;
        _parsers = parsers;
        _options = options;
    }

    public Task<IReadOnlyList<ListingSummary>> Collect(string searchTerm, Marketplace marketplace, CancellationToken ct)
    {
        _ = _client;
        _ = _urlServices;
        _ = _parsers;
        _ = _options;
        _ = searchTerm;
        _ = marketplace;
        _ = ct;
        throw new NotImplementedException();
    }
}
