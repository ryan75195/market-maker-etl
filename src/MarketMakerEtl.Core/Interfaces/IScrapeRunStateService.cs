using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeRunStateService
{
    void EnsureCanTransition(ScrapeRunStatus from, ScrapeRunStatus to);

    bool IsTerminal(ScrapeRunStatus status);
}
