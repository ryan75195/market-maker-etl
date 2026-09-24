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
    private readonly DetailFetchOptions _detailOptions;
    private readonly ILogger<SearchPageService> _logger;

    public SearchPageService(
        IScrapeClient client,
        MarketplaceAdapters adapters,
        ScrapeOptions options,
        DetailFetchOptions detailOptions,
        ILogger<SearchPageService> logger)
    {
        _client = client;
        _adapters = adapters;
        _options = options;
        _detailOptions = detailOptions;
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
        AddCapHitIssue(issues, searchTerm, sold: false, activeSummary);
        AddNoResultsIssue(issues, searchTerm, sold: false, activeSummary, merged.Count - beforeActive);

        var backfillFetches = 0;

        if (_options.CollectSold)
        {
            var beforeSold = merged.Count;
            var soldSummary = await CollectDirection(
                searchTerm, sold: true, urls, parser, itemParser, merged, knownSoldListingIds, ct);
            AddCapHitIssue(issues, searchTerm, sold: true, soldSummary);
            AddNoResultsIssue(issues, searchTerm, sold: true, soldSummary, merged.Count - beforeSold);
            AddBackfillWindowIssue(issues, searchTerm, _options.SoldBackfillDays, soldSummary);
            AddBackfillOverflowIssue(issues, searchTerm, soldSummary);
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
                ThrowIfListingMarkupProducedNoResults(parser, html);
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
            _client, itemParser, _options.SoldBackfillDays, _detailOptions.MaxDetailFetchesPerRun);
    }

    private static void AddCapHitIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        bool sold,
        PriceBandCollectionSummary? summary)
    {
        if (summary is not { CapHit: true })
        {
            return;
        }

        var direction = sold ? "sold" : "active";
        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "PriceBandCapHit",
            ErrorMessage: $"Hit the {summary.BandsFetched}-band cap while collecting '{searchTerm}' ({direction}).",
            Phase: "Search",
            HttpStatusCode: null));
    }

    private static void AddNoResultsIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        bool sold,
        PriceBandCollectionSummary? summary,
        int listingsAdded)
    {
        if (summary is not { BandsFetched: > 0, TotalReported: null } || listingsAdded > 0)
        {
            return;
        }

        var direction = sold ? "sold" : "active";
        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SearchYieldedNoResults",
            ErrorMessage: $"'{searchTerm}' ({direction}) fetched {summary.BandsFetched} band(s), collected 0 listings, and the search response carried no reported total; the scraper likely returned an unexpected payload shape rather than a genuinely empty search.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    private static void AddBackfillWindowIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        int soldBackfillDays,
        PriceBandCollectionSummary? summary)
    {
        if (summary?.BackfillCutoffUtc is not { } cutoff)
        {
            return;
        }

        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SoldBackfillWindow",
            ErrorMessage: $"Sold backfill for '{searchTerm}' covered the last {soldBackfillDays} day(s); cutoff {cutoff:O}.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    private static void AddBackfillOverflowIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        PriceBandCollectionSummary? summary)
    {
        if (summary is not { BandsOverCapacityUnsplit: > 0 })
        {
            return;
        }

        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SoldBackfillBandOverflow",
            ErrorMessage: $"{summary.BandsOverCapacityUnsplit} band(s) reported over 100 in-window sold listings for '{searchTerm}' but could not be split further; only the available page was stored.",
            Phase: "Search",
            HttpStatusCode: null));
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
