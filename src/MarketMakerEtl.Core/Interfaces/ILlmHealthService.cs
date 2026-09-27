using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Interfaces;

public interface ILlmHealthService
{
    Task<LlmHealthView> GetLlmHealth(CancellationToken ct);
}
