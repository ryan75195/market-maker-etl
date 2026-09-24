namespace MarketMakerEtl.Core.Interfaces;

public interface IListingRefreshSchedulingService
{
    Task RefreshListingsIfDue(CancellationToken ct);
}
