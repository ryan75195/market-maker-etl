using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface IListingRawDataStore
{
    Task<ListingRawData?> GetRawData(int listingEntityId, CancellationToken ct);
}
