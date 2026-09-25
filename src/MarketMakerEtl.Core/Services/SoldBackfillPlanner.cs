using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal enum SoldBackfillOutcomeKind
{
    None,
    Split,
    Store,
}

internal readonly record struct SoldBackfillDecision(SoldBackfillOutcomeKind Kind, int StoreCount, bool Overflowed)
{
    internal static SoldBackfillDecision None() => new(SoldBackfillOutcomeKind.None, 0, false);

    internal static SoldBackfillDecision Split() => new(SoldBackfillOutcomeKind.Split, 0, false);

    internal static SoldBackfillDecision Store(int count, bool overflowed) =>
        new(SoldBackfillOutcomeKind.Store, count, overflowed);
}

internal enum DateResolutionStatus
{
    Resolved,
    Unresolved,
}

internal readonly record struct DateLookup(DateResolutionStatus Status, DateTime? Value)
{
    internal static DateLookup Resolved(DateTime? value) => new(DateResolutionStatus.Resolved, value);

    internal static readonly DateLookup Unresolved = new(DateResolutionStatus.Unresolved, null);
}

internal readonly record struct ProbeSample(int Index, DateLookup When);

internal sealed class SoldBackfillPlanner
{
    private const int CutoffProbeMargin = 3;
    private const int FullPageSize = 100;
    private const int MaxFetchAttempts = 3;
    private const int LeadingOldSampleSize = 3;
    private const string SoldStatus = "Sold";

    private readonly IScrapeClient _client;
    private readonly IItemPageParser _itemParser;
    private readonly int _maxItemPageFetches;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, Task<DateLookup>> _dateCache = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();
    private int _itemPageFetches;

    internal SoldBackfillPlanner(
        IScrapeClient client,
        IItemPageParser itemParser,
        int soldBackfillDays,
        int maxItemPageFetches,
        TimeProvider timeProvider)
    {
        _client = client;
        _itemParser = itemParser;
        _maxItemPageFetches = maxItemPageFetches;
        _timeProvider = timeProvider;
        CutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-soldBackfillDays);
    }

    internal DateTime CutoffUtc { get; }

    internal int ItemPageFetchesUsed => Volatile.Read(ref _itemPageFetches);

    internal bool BudgetExhausted => Volatile.Read(ref _itemPageFetches) >= _maxItemPageFetches;

    internal async Task<SoldBackfillDecision> Resolve(
        SearchPageResult page, bool canSplit, CancellationToken ct, int concurrency = 1)
    {
        if (page.Listings.Count == 0)
        {
            return SoldBackfillDecision.None();
        }

        if (await IsEntirelyBeforeCutoff(page.Listings, ct))
        {
            return SoldBackfillDecision.None();
        }

        var lastIndex = page.Listings.Count - 1;
        var oldest = await DateOf(page.Listings[lastIndex], ct);
        var oldestInWindow = oldest.Status == DateResolutionStatus.Resolved && !IsBeforeCutoff(oldest.Value);
        var reportedCount = page.TotalCount ?? page.Listings.Count;
        var isFullPage = page.Listings.Count >= FullPageSize;

        if (oldestInWindow && isFullPage && reportedCount > FullPageSize)
        {
            return canSplit
                ? SoldBackfillDecision.Split()
                : SoldBackfillDecision.Store(page.Listings.Count, overflowed: true);
        }

        var cut = oldestInWindow ? page.Listings.Count : await FindCutoff(page.Listings, concurrency, ct);
        return SoldBackfillDecision.Store(cut, overflowed: false);
    }

    private bool IsBeforeCutoff(DateTime? when) => when is { } value && value < CutoffUtc;

    private async Task<bool> IsEntirelyBeforeCutoff(IReadOnlyList<ListingSummary> listings, CancellationToken ct)
    {
        var sampleSize = Math.Min(LeadingOldSampleSize, listings.Count);

        for (var i = 0; i < sampleSize; i++)
        {
            var when = await DateOf(listings[i], ct);
            if (when.Status != DateResolutionStatus.Resolved || !IsBeforeCutoff(when.Value))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<int> FindCutoff(IReadOnlyList<ListingSummary> items, int concurrency, CancellationToken ct)
    {
        var lo = 0;
        var hi = items.Count - 1;

        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            var when = await DateOf(items[mid], ct);

            if (when.Status == DateResolutionStatus.Unresolved || IsBeforeCutoff(when.Value))
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return await ProbeMargin(items, hi, concurrency, ct);
    }

    private async Task<int> ProbeMargin(
        IReadOnlyList<ListingSummary> items, int approximateCut, int concurrency, CancellationToken ct)
    {
        var lastInWindow = approximateCut - 1;
        var start = Math.Max(0, approximateCut - CutoffProbeMargin);
        var end = Math.Min(items.Count - 1, approximateCut + CutoffProbeMargin);

        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var probes = await Task.WhenAll(
            Enumerable.Range(start, end - start + 1).Select(i => ProbeOne(items[i], i, gate, ct)));

        foreach (var probe in probes)
        {
            if (probe.When.Status == DateResolutionStatus.Resolved && !IsBeforeCutoff(probe.When.Value))
            {
                lastInWindow = Math.Max(lastInWindow, probe.Index);
            }
        }

        return lastInWindow + 1;
    }

    private async Task<ProbeSample> ProbeOne(ListingSummary listing, int index, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var when = await DateOf(listing, ct);
            return new ProbeSample(index, when);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task<DateLookup> DateOf(ListingSummary listing, CancellationToken ct)
    {
        lock (_cacheLock)
        {
            if (_dateCache.TryGetValue(listing.ListingId, out var cached))
            {
                return cached;
            }

            var resolved = ResolveDate(listing, ct);
            _dateCache[listing.ListingId] = resolved;
            return resolved;
        }
    }

    private async Task<DateLookup> ResolveDate(ListingSummary listing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(listing.Url))
        {
            return DateLookup.Resolved(null);
        }

        for (var attempt = 0; attempt < MaxFetchAttempts; attempt++)
        {
            if (Volatile.Read(ref _itemPageFetches) >= _maxItemPageFetches)
            {
                return DateLookup.Unresolved;
            }

            var detail = await FetchDetail(listing.Url, ct);
            if (detail is not null)
            {
                return DateLookup.Resolved(ResolveSoldDate(detail));
            }
        }

        return DateLookup.Unresolved;
    }

    private async Task<ItemPageListing?> FetchDetail(string url, CancellationToken ct)
    {
        Interlocked.Increment(ref _itemPageFetches);

        try
        {
            var html = await _client.GetPageHtml(url, ct);
            return _itemParser.Parse(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private DateTime? ResolveSoldDate(ItemPageListing? detail)
    {
        if (detail is null)
        {
            return null;
        }

        var parsed = SoldDateParser.Parse(detail.SoldDate);
        if (parsed is not null)
        {
            return parsed;
        }

        return string.Equals(detail.Status, SoldStatus, StringComparison.Ordinal)
            ? _timeProvider.GetUtcNow().UtcDateTime
            : null;
    }
}
