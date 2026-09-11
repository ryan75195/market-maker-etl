using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class ScrapeRunStateService : IScrapeRunStateService
{
    private static readonly IReadOnlyDictionary<ScrapeRunStatus, ScrapeRunStatus[]> Allowed =
        new Dictionary<ScrapeRunStatus, ScrapeRunStatus[]>
        {
            [ScrapeRunStatus.Queued] = [ScrapeRunStatus.Running],
            [ScrapeRunStatus.Running] = [ScrapeRunStatus.Completed, ScrapeRunStatus.Failed],
            [ScrapeRunStatus.Completed] = [],
            [ScrapeRunStatus.Failed] = []
        };

    public void EnsureCanTransition(ScrapeRunStatus from, ScrapeRunStatus to)
    {
        if (!Allowed[from].Contains(to))
        {
            throw new InvalidOperationException($"Cannot move a run from {from} to {to}");
        }
    }

    public bool IsTerminal(ScrapeRunStatus status) =>
        status is ScrapeRunStatus.Completed or ScrapeRunStatus.Failed;
}
