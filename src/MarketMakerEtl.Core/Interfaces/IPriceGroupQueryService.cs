using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Interfaces;

public interface IPriceGroupQueryService
{
    Task<IReadOnlyList<PriceGroupSummary>> GetPriceGroups(PriceGroupQuery query, CancellationToken ct);

    Task<IReadOnlyList<PriceGroupListingResult>> GetGroupListings(PriceGroupListingsQuery query, CancellationToken ct);

    Task<IReadOnlyList<PriceGroupHistoryBucket>> GetPriceGroupHistory(PriceGroupHistoryQuery query, CancellationToken ct);

    Task<PriceGroupForwardWindowResult> GetForwardWindowStats(PriceGroupForwardWindowQuery query, CancellationToken ct);
}
