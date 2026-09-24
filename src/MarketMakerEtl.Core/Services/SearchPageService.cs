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

    public async Task<SearchCollectionResult> Collect(
        string searchTerm,
        Marketplace marketplace,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, ListingSummary>(StringComparer.Ordinal);
        var urls = SelectUrlService(marketplace);
        var parser = SelectParser(marketplace);
        var issues = new List<ScrapeRunIssueDetails>();

        var activeSummary = await CollectDirection(searchTerm, sold: false, urls, parser, merged, knownSoldListingIds, ct);
        AddCapHitIssue(issues, searchTerm, sold: false, activeSummary);

        if (_options.CollectSold)
        {
            var soldSummary = await CollectDirection(searchTerm, sold: true, urls, parser, merged, knownSoldListingIds, ct);
            AddCapHitIssue(issues, searchTerm, sold: true, soldSummary);
        }

        return new SearchCollectionResult(merged.Values.ToList(), activeSummary?.TotalReported, issues);
    }

    private IEbaySearchUrlService SelectUrlService(Marketplace marketplace) =>
        _urlServices.SingleOrDefault(service => service.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search URL implementation registered for marketplace {marketplace}.");

    private ISearchPageParser SelectParser(Marketplace marketplace) =>
        _parsers.SingleOrDefault(parser => parser.Marketplace == marketplace)
        ?? throw new InvalidOperationException(
            $"No search parser registered for marketplace {marketplace}.");

    private async Task<PriceBandCollectionSummary?> CollectDirection(
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
