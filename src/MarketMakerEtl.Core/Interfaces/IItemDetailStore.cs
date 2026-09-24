using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface IItemDetailStore
{
    Task<IReadOnlyList<ListingDetailTarget>> GetListingsNeedingDetail(int jobId, int limit, CancellationToken ct);

    Task ApplyItemDetail(int listingEntityId, ItemPageListing detail, CancellationToken ct);

    Task MarkDetailFetchFailed(int listingEntityId, CancellationToken ct);
}
