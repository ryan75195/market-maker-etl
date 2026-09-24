using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal sealed record PriceBandCollectionSummary(
    int BandsFetched,
    int? TotalReported,
    bool CapHit,
    int BandsPrunedForKnownListings);

internal sealed class PriceBandQueue
{
    private const int SeedPriority = int.MinValue;

    private readonly PriorityQueue<PriceBand, (int Rank, long Sequence)> _queue = new();
    private readonly HashSet<PriceBand> _seedBands;
    private long _sequence;

    private PriceBandQueue(IReadOnlyList<PriceBand> seedBands)
    {
        _seedBands = new HashSet<PriceBand>(seedBands);

        foreach (var band in seedBands)
        {
            Enqueue(band, SeedPriority);
        }
    }

    internal static PriceBandQueue SeededWithGeometricBands() => new(PriceBand.SeedBands());

    internal int Count => _queue.Count;

    internal bool IsSeedBand(PriceBand band) => _seedBands.Contains(band);

    internal PriceBand Dequeue() => _queue.Dequeue();

    internal void EnqueueChildren(PriceBand parent, int parentReportedCount)
    {
        foreach (var child in parent.Split())
        {
            Enqueue(child, -parentReportedCount);
        }
    }

    private void Enqueue(PriceBand band, int rank) =>
        _queue.Enqueue(band, (rank, _sequence++));
}

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
        var queue = PriceBandQueue.SeededWithGeometricBands();
        var pruneKnownBands = sold && knownSoldListingIds.Count > 0;
        var fetched = 0;
        var bandsPruned = 0;
        int? totalReported = null;

        while (queue.Count > 0 && fetched < _maxBandsPerDirection)
        {
            var band = queue.Dequeue();
            var result = await FetchBand(searchTerm, sold, band, ct);
            fetched++;

            if (queue.IsSeedBand(band))
            {
                totalReported = AccumulateReportedTotal(totalReported, result);
            }

            var newListingCount = MergeAndCountNew(result.Listings, merged, knownSoldListingIds);

            if (pruneKnownBands && newListingCount == 0)
            {
                bandsPruned++;
                continue;
            }

            if (ShouldSplit(result, band))
            {
                queue.EnqueueChildren(band, ReportedCount(result));
            }
        }

        var capHit = queue.Count > 0;
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

    private static int ReportedCount(SearchPageResult result) =>
        result.TotalCount ?? result.Listings.Count;

    private static int? AccumulateReportedTotal(int? totalReported, SearchPageResult result) =>
        result.TotalCount is int count ? (totalReported ?? 0) + count : totalReported;

    private static bool ShouldSplit(SearchPageResult result, PriceBand band) =>
        ReportedCount(result) >= MinimumCountRequiringSplit && band.CanSplit(MinimumBandWidth);

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
