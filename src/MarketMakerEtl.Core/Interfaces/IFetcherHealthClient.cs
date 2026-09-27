namespace MarketMakerEtl.Core.Interfaces;

public interface IFetcherHealthClient
{
    Task<bool> CheckSidecarReachable(CancellationToken ct);
}
