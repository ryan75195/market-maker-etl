using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyBacklogHealthService
{
    Task<IReadOnlyList<FamilyBacklogHealthView>> GetFamilyBacklogHealth(CancellationToken ct);
}
