using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDealSignalService
{
    Task<DealScanTickResult> ScanForDeals(CancellationToken ct);
}
