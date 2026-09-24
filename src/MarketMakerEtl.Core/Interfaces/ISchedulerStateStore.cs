namespace MarketMakerEtl.Core.Interfaces;

public interface ISchedulerStateStore
{
    Task<DateTime?> GetLastListingRefreshUtc(CancellationToken ct);

    Task SetLastListingRefreshUtc(DateTime timestampUtc, CancellationToken ct);
}
