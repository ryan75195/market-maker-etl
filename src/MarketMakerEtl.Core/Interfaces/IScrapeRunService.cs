using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeRunService
{
    Task Run(ScrapeRunWork work, CancellationToken ct);
}
