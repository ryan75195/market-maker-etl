namespace MarketMakerEtl.Core.Interfaces;

public interface IListingRefreshService
{
    Task RefreshActiveListings(CancellationToken ct);
}
