using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class StaleScrapeRunRecoveryService : IStaleScrapeRunRecoveryService
{
    private const string StaleRunningMessage =
        "Run was left Running by a previous ETL process (crash or kill); failed at startup.";

    private readonly IDbContextFactory<EtlDbContext> _factory;
    private readonly IScrapeRunStateService _states;

    public StaleScrapeRunRecoveryService(IDbContextFactory<EtlDbContext> factory, IScrapeRunStateService states)
    {
        _factory = factory;
        _states = states;
    }

    public async Task<int> FailRunsLeftRunning(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var staleRuns = await db.ScrapeRuns
            .Where(r => r.Status == nameof(ScrapeRunStatus.Running))
            .ToListAsync(ct);

        foreach (var run in staleRuns)
        {
            _states.EnsureCanTransition(ScrapeRunStatus.Running, ScrapeRunStatus.Failed);
            run.Status = nameof(ScrapeRunStatus.Failed);
            run.ErrorMessage = StaleRunningMessage;
            run.CompletedUtc = DateTime.UtcNow;
        }

        if (staleRuns.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return staleRuns.Count;
    }
}
