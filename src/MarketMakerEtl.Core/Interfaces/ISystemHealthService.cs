using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Interfaces;

public interface ISystemHealthService
{
    Task<SystemHealthResponse> GetHealth(CancellationToken ct);
}
