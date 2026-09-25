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
    bool BackfillBudgetExhausted = false,
    IReadOnlyList<SearchPageFailure>? SearchPageFailures = null);

internal sealed record MercariCollectionSettings(
    int MaxBandsPerDirection,
    SoldBackfillPlanner? Backfill,
    int SearchPageMaxAttempts = 5,
    int SearchPageRetryBaseDelaySeconds = 5,
    int SearchConcurrency = 1);

internal readonly record struct BandOutcome(SearchPageResult Result, bool Pruned, bool OverCapacity);

internal readonly record struct QueuedBand(PriceBand Band);

internal enum ClaimStatus
{
    Claimed,
    Wait,
    Done,
}

internal sealed class CollectorRunState
{
    internal readonly object SyncRoot = new();
    internal int Fetched;
    internal int InFlight;
    internal int BandsPruned;
    internal int BandsOverCapacityUnsplit;
    internal int? TotalReported;
    private TaskCompletionSource<bool> _signal = CreateSignal();

    internal Task CurrentSignalUnderLock()
    {
        lock (SyncRoot)
        {
            return _signal.Task;
        }
    }

    internal void PulseAll()
    {
        TaskCompletionSource<bool> previous;

        lock (SyncRoot)
        {
            previous = _signal;
            _signal = CreateSignal();
        }

        previous.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class PriceBandQueue
{
    private const int SeedPriority = int.MinValue;

    private readonly PriorityQueue<QueuedBand, (int Rank, long Sequence)> _queue = new();
    private readonly HashSet<PriceBand> _seedBands;
    private long _sequence;

    private PriceBandQueue(IReadOnlyList<PriceBand> seedBands)
    {
        _seedBands = new HashSet<PriceBand>(seedBands);

        foreach (var band in seedBands)
        {
            Enqueue(new QueuedBand(band), SeedPriority);
        }
    }

    internal static PriceBandQueue SeededWithGeometricBands() => new(PriceBand.SeedBands());

    internal int Count => _queue.Count;

    internal bool IsSeedBand(PriceBand band) => _seedBands.Contains(band);

    internal QueuedBand Dequeue() => _queue.Dequeue();

    internal void EnqueueChildren(PriceBand parent, int parentReportedCount)
    {
        foreach (var child in parent.Split())
        {
            Enqueue(new QueuedBand(child), -parentReportedCount);
        }
    }

    private void Enqueue(QueuedBand band, int rank) =>
        _queue.Enqueue(band, (rank, _sequence++));
}

internal static class PriceBandResultMath
{
    internal static int ReportedCount(SearchPageResult result) =>
        result.TotalCount ?? result.Listings.Count;

    internal static int? AccumulateReportedTotal(int? totalReported, SearchPageResult result) =>
        result.TotalCount is int count ? (totalReported ?? 0) + count : totalReported;
}

internal sealed class MercariPriceBandCollector
{
    private readonly IPriceBandSearchUrlService _urls;
    private readonly MercariCollectionSettings _settings;
    private readonly ILogger _logger;
    private readonly SearchPageFetcher _fetcher;

    internal MercariPriceBandCollector(
        IScrapeClient client,
        IPriceBandSearchUrlService urls,
        ISearchPageParser parser,
        MercariCollectionSettings settings,
        ILogger logger)
    {
        _urls = urls;
        _settings = settings;
        _logger = logger;
        _fetcher = new SearchPageFetcher(
            client,
            parser,
            settings.SearchPageMaxAttempts,
            settings.SearchPageRetryBaseDelaySeconds,
            logger);
    }

    internal Task<PriceBandCollectionSummary> Collect(
        string searchTerm,
        bool sold,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var run = new PriceBandCollectionRun(_urls, _settings, _logger, _fetcher);
        return run.Execute(searchTerm, sold, merged, knownSoldListingIds, ct);
    }
}

internal static class BandOutcomeApplier
{
    internal const decimal MinimumBandWidth = 0.01m;
    private const int MinimumCountRequiringSplit = 100;

    internal static BandOutcome ProcessSearchOutcome(
        PriceBand band,
        SearchPageResult result,
        PriceBandQueue queue,
        bool pruneKnownBands,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        bool sold,
        CollectorRunState state)
    {
        lock (state.SyncRoot)
        {
            var newListingCount = MergeAndCountNew(result.Listings, merged, knownSoldListingIds, sold);

            if (pruneKnownBands && newListingCount == 0)
            {
                return new BandOutcome(result, Pruned: true, OverCapacity: false);
            }

            if (ShouldSplit(result, band))
            {
                queue.EnqueueChildren(band, PriceBandResultMath.ReportedCount(result));
            }

            return new BandOutcome(result, Pruned: false, OverCapacity: false);
        }
    }

    internal static void ApplyBackfillDecision(
        SoldBackfillDecision decision,
        PriceBand band,
        SearchPageResult result,
        PriceBandQueue queue,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        bool sold)
    {
        switch (decision.Kind)
        {
            case SoldBackfillOutcomeKind.Split:
                queue.EnqueueChildren(band, PriceBandResultMath.ReportedCount(result));
                break;
            case SoldBackfillOutcomeKind.Store:
                MergeAndCountNew(result.Listings.Take(decision.StoreCount).ToList(), merged, knownSoldListingIds, sold);
                break;
            case SoldBackfillOutcomeKind.None:
            default:
                break;
        }
    }

    private static int MergeAndCountNew(
        IReadOnlyList<ListingSummary> listings,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        bool forceSold)
    {
        var newCount = 0;

        foreach (var listing in listings)
        {
            if (!knownSoldListingIds.Contains(listing.ListingId))
            {
                newCount++;
            }

            merged[listing.ListingId] = forceSold ? listing with { IsSold = true } : listing;
        }

        return newCount;
    }

    private static bool ShouldSplit(SearchPageResult result, PriceBand band) =>
        PriceBandResultMath.ReportedCount(result) >= MinimumCountRequiringSplit && band.CanSplit(MinimumBandWidth);
}

internal sealed class PriceBandWorkerPool
{
    private readonly int _maxBandsPerDirection;

    internal PriceBandWorkerPool(int maxBandsPerDirection) => _maxBandsPerDirection = maxBandsPerDirection;

    internal async Task RunAsync(
        int concurrency,
        PriceBandQueue queue,
        CollectorRunState state,
        Func<QueuedBand, PriceBandQueue, CancellationToken, Task<BandOutcome?>> processBand,
        CancellationToken ct)
    {
        using var faultCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => RunWorker(queue, state, faultCts, processBand)));
    }

    private async Task RunWorker(
        PriceBandQueue queue,
        CollectorRunState state,
        CancellationTokenSource faultCts,
        Func<QueuedBand, PriceBandQueue, CancellationToken, Task<BandOutcome?>> processBand)
    {
        try
        {
            while (true)
            {
                var status = ClaimNext(queue, state, out var queued, out var waitTask);
                if (status == ClaimStatus.Done)
                {
                    return;
                }

                if (status == ClaimStatus.Wait)
                {
                    await waitTask.WaitAsync(faultCts.Token);
                    continue;
                }

                await ProcessClaimedBand(queued, queue, state, processBand, faultCts.Token);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await faultCts.CancelAsync();
            throw;
        }
    }

    private ClaimStatus ClaimNext(PriceBandQueue queue, CollectorRunState state, out QueuedBand queued, out Task waitTask)
    {
        lock (state.SyncRoot)
        {
            if (state.Fetched >= _maxBandsPerDirection)
            {
                queued = default;
                waitTask = Task.CompletedTask;
                return ClaimStatus.Done;
            }

            if (queue.Count > 0)
            {
                queued = queue.Dequeue();
                state.Fetched++;
                state.InFlight++;
                waitTask = Task.CompletedTask;
                return ClaimStatus.Claimed;
            }

            queued = default;

            if (state.InFlight > 0)
            {
                waitTask = state.CurrentSignalUnderLock();
                return ClaimStatus.Wait;
            }

            waitTask = Task.CompletedTask;
            return ClaimStatus.Done;
        }
    }

    private static async Task ProcessClaimedBand(
        QueuedBand queued,
        PriceBandQueue queue,
        CollectorRunState state,
        Func<QueuedBand, PriceBandQueue, CancellationToken, Task<BandOutcome?>> processBand,
        CancellationToken ct)
    {
        var isSeedBand = queue.IsSeedBand(queued.Band);

        try
        {
            var outcome = await processBand(queued, queue, ct);
            if (outcome is not { } bandOutcome)
            {
                return;
            }

            lock (state.SyncRoot)
            {
                if (isSeedBand)
                {
                    state.TotalReported = PriceBandResultMath.AccumulateReportedTotal(state.TotalReported, bandOutcome.Result);
                }

                state.BandsPruned += bandOutcome.Pruned ? 1 : 0;
                state.BandsOverCapacityUnsplit += bandOutcome.OverCapacity ? 1 : 0;
            }
        }
        finally
        {
            lock (state.SyncRoot)
            {
                state.InFlight--;
            }

            state.PulseAll();
        }
    }
}

internal sealed class PriceBandCollectionRun
{
    private readonly IPriceBandSearchUrlService _urls;
    private readonly MercariCollectionSettings _settings;
    private readonly ILogger _logger;
    private readonly SearchPageFetcher _fetcher;
    private readonly List<SearchPageFailure> _searchPageFailures = [];

    internal PriceBandCollectionRun(
        IPriceBandSearchUrlService urls,
        MercariCollectionSettings settings,
        ILogger logger,
        SearchPageFetcher fetcher)
    {
        _urls = urls;
        _settings = settings;
        _logger = logger;
        _fetcher = fetcher;
    }

    internal async Task<PriceBandCollectionSummary> Execute(
        string searchTerm,
        bool sold,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct)
    {
        var queue = PriceBandQueue.SeededWithGeometricBands();
        var pruneKnownBands = sold && knownSoldListingIds.Count > 0;
        var backfill = sold && knownSoldListingIds.Count == 0 ? _settings.Backfill : null;
        var state = new CollectorRunState();
        var concurrency = Math.Max(1, _settings.SearchConcurrency);

        var pool = new PriceBandWorkerPool(_settings.MaxBandsPerDirection);
        await pool.RunAsync(
            concurrency,
            queue,
            state,
            (queued, q, token) => ProcessBand(
                searchTerm, sold, queued, q, backfill, pruneKnownBands, merged, knownSoldListingIds, state, token),
            ct);

        var capHit = queue.Count > 0;
        LogOutcome(searchTerm, sold, state.Fetched, merged.Count, state.TotalReported, capHit, state.BandsPruned);

        var itemPageFetchesUsed = backfill?.ItemPageFetchesUsed ?? 0;
        var backfillBudgetExhausted = backfill?.BudgetExhausted ?? false;

        return new PriceBandCollectionSummary(
            state.Fetched,
            state.TotalReported,
            capHit,
            state.BandsPruned,
            itemPageFetchesUsed,
            backfill?.CutoffUtc,
            state.BandsOverCapacityUnsplit,
            backfillBudgetExhausted,
            _searchPageFailures);
    }

    private async Task<BandOutcome?> ProcessBand(
        string searchTerm,
        bool sold,
        QueuedBand queued,
        PriceBandQueue queue,
        SoldBackfillPlanner? backfill,
        bool pruneKnownBands,
        Dictionary<string, ListingSummary> merged,
        IReadOnlySet<string> knownSoldListingIds,
        CollectorRunState state,
        CancellationToken ct)
    {
        var band = queued.Band;
        var result = await FetchBand(searchTerm, sold, band, state, ct);
        if (result is null)
        {
            return null;
        }

        if (backfill is not null)
        {
            var decision = await backfill.Resolve(
                result, band.CanSplit(BandOutcomeApplier.MinimumBandWidth), ct, _settings.SearchConcurrency);
            LogBackfillDecision(band, result, decision, backfill);

            lock (state.SyncRoot)
            {
                BandOutcomeApplier.ApplyBackfillDecision(decision, band, result, queue, merged, knownSoldListingIds, sold);
            }

            return new BandOutcome(result, Pruned: false, OverCapacity: decision.Overflowed);
        }

        return BandOutcomeApplier.ProcessSearchOutcome(
            band, result, queue, pruneKnownBands, merged, knownSoldListingIds, sold, state);
    }

    private async Task<SearchPageResult?> FetchBand(
        string searchTerm, bool sold, PriceBand band, CollectorRunState state, CancellationToken ct)
    {
        var url = _urls.BuildSearch(searchTerm, sold, band.MinPrice, band.MaxPrice);
        var outcome = await _fetcher.Fetch(url, band, ct);

        if (outcome.Failure is { } failure)
        {
            lock (state.SyncRoot)
            {
                _searchPageFailures.Add(failure);
            }
        }

        return outcome.Result;
    }

    private void LogBackfillDecision(
        PriceBand band,
        SearchPageResult result,
        SoldBackfillDecision decision,
        SoldBackfillPlanner backfill)
    {
        _logger.LogInformation(
            "Sold backfill decision for band [{MinPrice}-{MaxPrice}]: reported {Reported}, page size {PageSize}, "
                + "decision {Decision}, stored {Stored}, item-page fetches used {ItemPageFetches}.",
            band.MinPrice, band.MaxPrice, PriceBandResultMath.ReportedCount(result), result.Listings.Count,
            decision.Kind, decision.StoreCount, backfill.ItemPageFetchesUsed);
    }

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
