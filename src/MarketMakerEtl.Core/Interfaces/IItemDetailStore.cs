using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface IItemDetailStore
{
    Task<IReadOnlyList<ListingDetailTarget>> GetListingsNeedingDetail(
        int jobId, int limit, int maxAttempts, CancellationToken ct);

    Task<IReadOnlyDictionary<string, int>> GetListingEntityIds(
        int jobId, IReadOnlyCollection<string> listingIds, CancellationToken ct);

    Task ApplyItemDetail(int listingEntityId, ItemPageListing detail, CancellationToken ct);

    Task MarkDetailFetchFailed(int listingEntityId, int maxAttempts, CancellationToken ct);
}
