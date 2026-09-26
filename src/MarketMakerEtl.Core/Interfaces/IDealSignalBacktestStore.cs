using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDealSignalBacktestStore
{
    Task<IReadOnlyList<DealSignalEvaluationCandidate>> GetSignalsPendingEvaluation(
        DateTime nowUtc, int horizonDays, int minForwardSoldCount, CancellationToken ct);

    Task<DealSignalListingSaleInfo?> GetListingSaleInfo(int listingEntityId, CancellationToken ct);

    Task ApplyEvaluations(IReadOnlyList<DealSignalEvaluationResult> results, CancellationToken ct);
}
