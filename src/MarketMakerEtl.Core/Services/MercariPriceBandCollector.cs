using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal sealed record PriceBandCollectionSummary(
    int BandsFetched,
    int? TotalReported,
    bool CapHit,
    int BandsPrunedForKnownListings,
    int ItemPageFetchesUsed = 0,
    DateTime? BackfillCutoffUtc = null,
    int BandsOverCapacityUnsplit = 0,
    bool BackfillBudgetExhausted = false);

internal sealed record MercariCollectionSettings(int MaxBandsPerDirection, SoldBackfillPlanner? Backfill);

internal readonly record struct BandOutcome(SearchPageResult Result, bool Pruned, bool OverCapacity);

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
    private readonly MercariCollectionSettings _settings;
    private readonly ILogger _logger;

    internal MercariPriceBandCollector(
        IScrapeClient client,
        IPriceBandSearchUrlService urls,
        ISearchPageParser parser,
        MercariCollectionSettings settings,
        ILogger logger)
    {
        _client = client;
        _urls = urls;
        _parser = parser;
        _settings = settings;
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
        var backfill = sold && knownSoldListingIds.Count == 0 ? _settings.Backfill : null;
        var fetched = 0;
        var bandsPruned = 0;
        var bandsOverCapacityUnsplit = 0;
        int? totalReported = null;

        while (queue.Count > 0 && fetched < _settings.MaxBandsPerDirection)
        {
            var band = queue.Dequeue();
            var outcome = await ProcessBand(
                searchTerm, sold, band, queue, backfill, pruneKnownBands, merged, knownSoldListingIds, ct);
            fetched++;

            if (queue.IsSeedBand(band))
            {
                totalReported = AccumulateReportedTotal(totalReported, outcome.Result);
            }

            bandsPruned += outcome.Pruned ? 1 : 0;
            bandsOverCapacityUnsplit += outcome.OverCapacity ? 1 : 0;
        }

        var capHit = queue.Count > 0;
        LogOutcome(searchTerm, sold, fetched, merged.Count, totalReported, capHit, bandsPruned);

        var itemPageFetchesUsed = backfill is null ? 0 : backfill.ItemPageFetchesUsed;
        var backfillBudgetExhausted = backfill is not null && backfill.BudgetExhausted;

        return new PriceBandCollectionSummary(
            fetched,
            totalReported,
            capHit,
            bandsPruned,
            itemPageFetchesUsed,
            backfill?.CutoffUtc,
            bandsOverCapacityUnsplit,
            backfillBudgetExhausted);
    }

    private async Task<BandOutcome> ProcessBand(
        string searchTerm,
        bool sold,
        PriceBand band,
        PriceBandQueue queue,
        SoldBackfillPlanner? backfill,
        bool pruneKnownBands,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var result = await FetchBand(searchTerm, sold, band, ct);

        if (backfill is not null)
        {
            var decision = await backfill.Resolve(result, band.CanSplit(MinimumBandWidth), ct);
            ApplyBackfillDecision(decision, band, result, queue, merged, knownSoldListingIds);
            return new BandOutcome(result, Pruned: false, OverCapacity: decision.Overflowed);
        }

        var newListingCount = MergeAndCountNew(result.Listings, merged, knownSoldListingIds);

        if (pruneKnownBands && newListingCount == 0)
        {
            return new BandOutcome(result, Pruned: true, OverCapacity: false);
        }

        if (ShouldSplit(result, band))
        {
            queue.EnqueueChildren(band, ReportedCount(result));
        }

        return new BandOutcome(result, Pruned: false, OverCapacity: false);
    }

    private static void ApplyBackfillDecision(
        SoldBackfillDecision decision,
        PriceBand band,
        SearchPageResult result,
        PriceBandQueue queue,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds)
    {
        switch (decision.Kind)
        {
            case SoldBackfillOutcomeKind.Split:
                queue.EnqueueChildren(band, ReportedCount(result));
                break;
            case SoldBackfillOutcomeKind.Store:
                MergeAndCountNew(result.Listings.Take(decision.StoreCount).ToList(), merged, knownSoldListingIds);
                break;
            case SoldBackfillOutcomeKind.None:
            default:
                break;
        }
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
                searchTerm, direction, _settings.MaxBandsPerDirection, fetched);
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
