using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

public sealed class DealSignalService : IDealSignalService
{
    private const int DefaultSoldDays = 30;
    private const int MaxActiveListingsPerGroup = 500;

    private static readonly IReadOnlyDictionary<string, string> EmptyWhere =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IProductFamilyStore _families;
    private readonly IPriceGroupQueryService _priceGroups;
    private readonly IDealSignalStore _signals;
    private readonly IDealWebhookClient _webhook;

    public DealSignalService(
        IProductFamilyStore families,
        IPriceGroupQueryService priceGroups,
        IDealSignalStore signals,
        IDealWebhookClient webhook)
    {
        _families = families;
        _priceGroups = priceGroups;
        _signals = signals;
        _webhook = webhook;
    }

    public async Task<DealScanTickResult> ScanForDeals(CancellationToken ct)
    {
        var families = await _families.GetFamilies(ct);
        var dealFamilies = families.Where(IsDealEnabled).ToList();

        var groupsEvaluated = 0;
        var signalsCreated = 0;
        foreach (var family in dealFamilies)
        {
            var result = await ScanFamily(family, ct);
            groupsEvaluated += result.GroupsEvaluated;
            signalsCreated += result.SignalsCreated;
        }

        return new DealScanTickResult(dealFamilies.Count, groupsEvaluated, signalsCreated);
    }

    private static bool IsDealEnabled(ProductFamilyView family) =>
        !string.IsNullOrWhiteSpace(family.DealGroupBy) && family.LatestTaxonomyVersion is not null;

    private async Task<DealFamilyScanResult> ScanFamily(ProductFamilyView family, CancellationToken ct)
    {
        var taxonomyVersionId = family.LatestTaxonomyVersion!.Id;
        var groupBy = SplitGroupBy(family.DealGroupBy!);
        var query = new PriceGroupQuery(
            taxonomyVersionId, EmptyWhere, groupBy, DefaultSoldDays, false, family.DealMinSold, true);
        var summaries = await _priceGroups.GetPriceGroups(query, ct);

        var signalsCreated = 0;
        foreach (var summary in summaries)
        {
            signalsCreated += await ScanGroup(family, taxonomyVersionId, summary, ct);
        }

        return new DealFamilyScanResult(summaries.Count, signalsCreated);
    }

    private async Task<int> ScanGroup(
        ProductFamilyView family, int taxonomyVersionId, PriceGroupSummary summary, CancellationToken ct)
    {
        if (summary.SoldNetMedian is not decimal soldNetMedian)
        {
            return 0;
        }

        var listingsQuery = new PriceGroupListingsQuery(
            taxonomyVersionId,
            summary.Key,
            PriceGroupListingStatus.Active,
            MaxActiveListingsPerGroup,
            DefaultSoldDays,
            false);
        var listings = await _priceGroups.GetGroupListings(listingsQuery, ct);

        var created = 0;
        foreach (var listing in listings)
        {
            if (await TryCreateSignal(family, taxonomyVersionId, summary, soldNetMedian, listing, ct))
            {
                created++;
            }
        }

        return created;
    }

    private async Task<bool> TryCreateSignal(
        ProductFamilyView family,
        int taxonomyVersionId,
        PriceGroupSummary summary,
        decimal soldNetMedian,
        PriceGroupListingResult listing,
        CancellationToken ct)
    {
        if (listing.LandedPrice is not decimal landedPrice)
        {
            return false;
        }

        var discount = DealDiscountCalculator.Calculate(soldNetMedian, landedPrice);
        var meetsThreshold = DealDiscountCalculator.MeetsThreshold(
            summary.SoldCount, discount, family.DealMinSold, family.DealMinDiscount);
        if (!meetsThreshold)
        {
            return false;
        }

        var candidate = new DealSignalCandidate(
            listing.ListingId,
            family.Id,
            taxonomyVersionId,
            summary.Key,
            landedPrice,
            soldNetMedian,
            summary.SoldCount,
            summary.SoldP25,
            discount);
        var inserted = await _signals.TryInsertSignal(candidate, ct);
        if (!inserted)
        {
            return false;
        }

        await NotifyWebhook(listing, summary.Key, landedPrice, soldNetMedian, discount, ct);
        return true;
    }

    private async Task NotifyWebhook(
        PriceGroupListingResult listing,
        IReadOnlyDictionary<string, string> groupKey,
        decimal landedPrice,
        decimal soldNetMedian,
        decimal discount,
        CancellationToken ct)
    {
        try
        {
            var payload = new DealWebhookPayload(
                listing.Title, listing.Url, landedPrice, groupKey, soldNetMedian, discount);
            await _webhook.Notify(payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    private static IReadOnlyList<string> SplitGroupBy(string dealGroupBy) =>
        dealGroupBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
