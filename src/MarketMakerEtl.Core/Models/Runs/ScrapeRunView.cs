namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ScrapeRunView(
    int RunId,
    int JobId,
    string SearchTerm,
    ScrapeRunStatus Status,
    string? ErrorMessage);
