using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFetcherHealthService
{
    Task<FetcherHealthView> GetFetcherHealth(CancellationToken ct);
}
