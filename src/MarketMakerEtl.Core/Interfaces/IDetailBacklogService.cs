using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDetailBacklogService
{
    Task<DetailBacklogTickResult> RunTick(CancellationToken ct);
}
