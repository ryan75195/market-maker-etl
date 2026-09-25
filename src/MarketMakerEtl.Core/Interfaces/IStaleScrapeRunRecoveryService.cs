namespace MarketMakerEtl.Core.Interfaces;

public interface IStaleScrapeRunRecoveryService
{
    Task<int> FailRunsLeftRunning(CancellationToken ct);
}
