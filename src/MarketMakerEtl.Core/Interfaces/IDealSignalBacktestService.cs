using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDealSignalBacktestService
{
    Task<DealBacktestTickResult> EvaluateSignals(CancellationToken ct);
}
