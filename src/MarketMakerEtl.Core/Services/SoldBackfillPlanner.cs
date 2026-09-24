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

internal sealed class SoldBackfillPlanner
{
    private const int CutoffProbeMargin = 3;
    private const int FullPageSize = 100;
    private const string SoldStatus = "Sold";

    private readonly IScrapeClient _client;
    private readonly IItemPageParser _itemParser;
    private readonly int _maxItemPageFetches;
    private readonly Dictionary<string, DateTime?> _dateCache = new(StringComparer.Ordinal);
    private int _itemPageFetches;

    internal SoldBackfillPlanner(IScrapeClient client, IItemPageParser itemParser, int soldBackfillDays, int maxItemPageFetches)
    {
        _client = client;
        _itemParser = itemParser;
        _maxItemPageFetches = maxItemPageFetches;
        CutoffUtc = DateTime.UtcNow.AddDays(-soldBackfillDays);
    }

    internal DateTime CutoffUtc { get; }

    internal int ItemPageFetchesUsed => _itemPageFetches;

    internal async Task<SoldBackfillDecision> Resolve(SearchPageResult page, bool canSplit, CancellationToken ct)
    {
        if (page.Listings.Count == 0)
        {
            return SoldBackfillDecision.None();
        }

        var newest = await DateOf(page.Listings[0], ct);
        if (IsBeforeCutoff(newest))
        {
            return SoldBackfillDecision.None();
        }

        var lastIndex = page.Listings.Count - 1;
        var oldest = await DateOf(page.Listings[lastIndex], ct);
        var oldestInWindow = !IsBeforeCutoff(oldest);
        var reportedCount = page.TotalCount ?? page.Listings.Count;
        var isFullPage = page.Listings.Count >= FullPageSize;

        if (oldestInWindow && isFullPage && reportedCount > FullPageSize)
        {
            return canSplit
                ? SoldBackfillDecision.Split()
                : SoldBackfillDecision.Store(page.Listings.Count, overflowed: true);
        }

        var cut = oldestInWindow ? page.Listings.Count : await FindCutoff(page.Listings, ct);
        return SoldBackfillDecision.Store(cut, overflowed: false);
    }

    private bool IsBeforeCutoff(DateTime? when) => when is { } value && value < CutoffUtc;

    private async Task<int> FindCutoff(IReadOnlyList<ListingSummary> items, CancellationToken ct)
    {
        var lo = 0;
        var hi = items.Count - 1;

        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            var when = await DateOf(items[mid], ct);

            if (IsBeforeCutoff(when))
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return await ProbeMargin(items, hi, ct);
    }

    private async Task<int> ProbeMargin(IReadOnlyList<ListingSummary> items, int approximateCut, CancellationToken ct)
    {
        var lastInWindow = approximateCut - 1;
        var start = Math.Max(0, approximateCut - CutoffProbeMargin);
        var end = Math.Min(items.Count - 1, approximateCut + CutoffProbeMargin);

        for (var i = start; i <= end; i++)
        {
            var when = await DateOf(items[i], ct);
            if (!IsBeforeCutoff(when))
            {
                lastInWindow = Math.Max(lastInWindow, i);
            }
        }

        return lastInWindow + 1;
    }

    private async Task<DateTime?> DateOf(ListingSummary listing, CancellationToken ct)
    {
        if (_dateCache.TryGetValue(listing.ListingId, out var cached))
        {
            return cached;
        }

        var resolved = await ResolveDate(listing, ct);
        _dateCache[listing.ListingId] = resolved;
        return resolved;
    }

    private async Task<DateTime?> ResolveDate(ListingSummary listing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(listing.Url) || _itemPageFetches >= _maxItemPageFetches)
        {
            return null;
        }

        _itemPageFetches++;

        try
        {
            var html = await _client.GetPageHtml(listing.Url, ct);
            var detail = _itemParser.Parse(html);
            return ResolveSoldDate(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static DateTime? ResolveSoldDate(ItemPageListing? detail)
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

        return string.Equals(detail.Status, SoldStatus, StringComparison.Ordinal) ? DateTime.UtcNow : null;
    }
}
