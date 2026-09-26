using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDealSignalStore
{
    Task<bool> TryInsertSignal(DealSignalCandidate candidate, CancellationToken ct);

    Task<IReadOnlyList<DealSignalView>> GetSignals(
        int productFamilyId, DateTime? since, int take, CancellationToken ct);

    Task<DealPerformanceReport> GetPerformance(int productFamilyId, CancellationToken ct);
}
