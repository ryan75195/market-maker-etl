namespace MarketMakerEtl.Core.Models.Runs;

public sealed record JobLastRunView(
    ScrapeRunStatus Status,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    DateTime? LastCompletedRunUtc);
