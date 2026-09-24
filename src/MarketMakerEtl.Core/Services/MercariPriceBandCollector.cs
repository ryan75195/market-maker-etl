using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal sealed record PriceBandCollectionSummary(
    int BandsFetched,
    int? TotalReported,
    bool CapHit,
    int BandsPrunedForKnownListings);

internal sealed class MercariPriceBandCollector
{
    private const decimal MinimumBandWidth = 0.01m;
    private const int MinimumCountRequiringSplit = 100;

    private readonly IScrapeClient _client;
    private readonly IPriceBandSearchUrlService _urls;
    private readonly ISearchPageParser _parser;
    private readonly int _maxBandsPerDirection;
    private readonly ILogger _logger;

    internal MercariPriceBandCollector(
        IScrapeClient client,
        IPriceBandSearchUrlService urls,
        ISearchPageParser parser,
        int maxBandsPerDirection,
        ILogger logger)
    {
        _client = client;
        _urls = urls;
        _parser = parser;
        _maxBandsPerDirection = maxBandsPerDirection;
        _logger = logger;
    }

    internal async Task<PriceBandCollectionSummary> Collect(
        string searchTerm,
        bool sold,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var bands = new Queue<PriceBand>();
        bands.Enqueue(PriceBand.Unfiltered);

        var pruneKnownBands = sold && knownSoldListingIds.Count > 0;
        var fetched = 0;
        var capHit = false;
        var bandsPruned = 0;
        int? totalReported = null;

        while (bands.Count > 0)
        {
            if (fetched >= _maxBandsPerDirection)
            {
                capHit = true;
                break;
            }

            var band = bands.Dequeue();
            var result = await FetchBand(searchTerm, sold, band, ct);
            fetched++;
            totalReported ??= result.TotalCount;

            var newListingCount = MergeAndCountNew(result.Listings, merged, knownSoldListingIds);

            if (pruneKnownBands && newListingCount == 0)
            {
                bandsPruned++;
                continue;
            }

            if (ShouldSplit(result, band))
            {
                foreach (var child in band.Split())
                {
                    bands.Enqueue(child);
                }
            }
        }

        LogOutcome(searchTerm, sold, fetched, merged.Count, totalReported, capHit, bandsPruned);

        return new PriceBandCollectionSummary(fetched, totalReported, capHit, bandsPruned);
    }

    private async Task<SearchPageResult> FetchBand(string searchTerm, bool sold, PriceBand band, CancellationToken ct)
    {
        var url = _urls.BuildSearch(searchTerm, sold, band.MinPrice, band.MaxPrice);
        var html = await _client.GetPageHtml(url, ct);
        return _parser.Parse(html);
    }

    private static int MergeAndCountNew(
        IReadOnlyList<ListingSummary> listings,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds)
    {
        var newCount = 0;

        foreach (var listing in listings)
        {
            if (!knownSoldListingIds.Contains(listing.ListingId))
            {
                newCount++;
            }

            merged[listing.ListingId] = listing;
        }

        return newCount;
    }

    private static bool ShouldSplit(SearchPageResult result, PriceBand band) =>
        (result.TotalCount ?? result.Listings.Count) >= MinimumCountRequiringSplit
        && band.CanSplit(MinimumBandWidth);

    private void LogOutcome(
        string searchTerm,
        bool sold,
        int fetched,
        int collected,
        int? totalReported,
        bool capHit,
        int bandsPruned)
    {
        var direction = sold ? "sold" : "active";

        if (capHit)
        {
            _logger.LogWarning(
                "Mercari band search for '{SearchTerm}' ({Direction}) hit the {MaxBands} band cap after {Fetched} fetches.",
                searchTerm, direction, _maxBandsPerDirection, fetched);
        }

        if (bandsPruned > 0)
        {
            _logger.LogInformation(
                "Mercari sold band search for '{SearchTerm}' pruned {BandsPruned} band(s) that yielded no new listings.",
                searchTerm, bandsPruned);
        }

        _logger.LogInformation(
            "Mercari band search for '{SearchTerm}' ({Direction}) fetched {Fetched} bands, reported {Reported}, collected {Collected}.",
            searchTerm, direction, fetched, totalReported, collected);
    }
}
