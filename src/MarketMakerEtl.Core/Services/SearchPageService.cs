using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class SearchPageService : ISearchPageService
{
    private readonly IScrapeClient _client;
    private readonly MarketplaceAdapters _adapters;
    private readonly ScrapeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SearchPageService> _logger;

    public SearchPageService(
        IScrapeClient client,
        MarketplaceAdapters adapters,
        ScrapeOptions options,
        TimeProvider timeProvider,
        ILogger<SearchPageService> logger)
    {
        _client = client;
        _adapters = adapters;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SearchCollectionResult> Collect(
        string searchTerm,
        Marketplace marketplace,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, ListingSummary>(StringComparer.Ordinal);
        var urls = SelectUrlService(marketplace);
        var parser = SelectParser(marketplace);
        var itemParser = SelectItemParser(marketplace);
        var issues = new List<ScrapeRunIssueDetails>();

        var beforeActive = merged.Count;
        var activeSummary = await CollectDirection(
            searchTerm, sold: false, urls, parser, itemParser, merged, knownSoldListingIds, ct);
        SearchRunIssueFactory.AddCapHitIssue(issues, searchTerm, sold: false, activeSummary);
        SearchRunIssueFactory.AddNoResultsIssue(issues, searchTerm, sold: false, activeSummary, merged.Count - beforeActive);

        var backfillFetches = 0;

        if (_options.CollectSold)
        {
            var beforeSold = merged.Count;
            var soldSummary = await CollectDirection(
                searchTerm, sold: true, urls, parser, itemParser, merged, knownSoldListingIds, ct);
            SearchRunIssueFactory.AddCapHitIssue(issues, searchTerm, sold: true, soldSummary);
            SearchRunIssueFactory.AddNoResultsIssue(issues, searchTerm, sold: true, soldSummary, merged.Count - beforeSold);
            SearchRunIssueFactory.AddBackfillWindowIssue(issues, searchTerm, _options.SoldBackfillDays, soldSummary);
            SearchRunIssueFactory.AddBackfillOverflowIssue(issues, searchTerm, soldSummary);
            SearchRunIssueFactory.AddBackfillBudgetExhaustedIssue(issues, searchTerm, soldSummary);
            backfillFetches = soldSummary?.ItemPageFetchesUsed ?? 0;
        }

        return new SearchCollectionResult(merged.Values.ToList(), activeSummary?.TotalReported, issues, backfillFetches);
    }

    private IEbaySearchUrlService SelectUrlService(Marketplace marketplace) =>
        _adapters.UrlServices.SingleOrDefault(service => service.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search URL implementation registered for marketplace {marketplace}.");

    private ISearchPageParser SelectParser(Marketplace marketplace) =>
        _adapters.SearchParsers.SingleOrDefault(parser => parser.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search parser registered for marketplace {marketplace}.");

    private IItemPageParser? SelectItemParser(Marketplace marketplace) =>
        _adapters.ItemParsers.SingleOrDefault(parser => parser.Marketplace == marketplace);

    private async Task<PriceBandCollectionSummary?> CollectDirection(
        string searchTerm,
        bool sold,
        IEbaySearchUrlService urls,
        ISearchPageParser parser,
        IItemPageParser? itemParser,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        if (urls is IPriceBandSearchUrlService bandUrls)
        {
            var settings = new MercariCollectionSettings(
                _options.MaxBandsPerDirection,
                BuildBackfillPlanner(sold, itemParser, knownSoldListingIds));
            var collector = new MercariPriceBandCollector(_client, bandUrls, parser, settings, _logger);
            return await collector.Collect(searchTerm, sold, merged, knownSoldListingIds, ct);
        }

        var pageLimit = urls.SupportsPagination ? _options.MaxPages : 1;

        for (var page = 1; page <= pageLimit; page++)
        {
            var url = urls.BuildSearch(searchTerm, sold, page);
            var html = await _client.GetPageHtml(url, ct);
            var pageResult = parser.Parse(html);

            if (pageResult.Listings.Count == 0)
            {
                SearchRunIssueFactory.ThrowIfListingMarkupProducedNoResults(parser, html);
                return null;
            }

            foreach (var listing in pageResult.Listings)
            {
                merged[listing.ListingId] = listing;
            }
        }

        return null;
    }

    private SoldBackfillPlanner? BuildBackfillPlanner(
        bool sold, IItemPageParser? itemParser, IReadOnlySet<string> knownSoldListingIds)
    {
        if (!sold || itemParser is null || knownSoldListingIds.Count > 0)
        {
            return null;
        }

        return new SoldBackfillPlanner(
            _client, itemParser, _options.SoldBackfillDays, _options.MaxBackfillItemPageFetches, _timeProvider);
    }
}
