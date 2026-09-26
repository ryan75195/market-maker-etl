using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Models.Health;

public sealed record JobHealthView(
    int JobId,
    string SearchTerm,
    bool IsEnabled,
    ScrapeRunStatus? LastRunStatus,
    DateTime? LastRunStartedUtc,
    DateTime? LastRunCompletedUtc,
    bool IsStale);
