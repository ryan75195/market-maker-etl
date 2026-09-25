namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ScrapeRunView(
    int RunId,
    int JobId,
    string SearchTerm,
    ScrapeRunStatus Status,
    string? ErrorMessage,
    TriggerType TriggerType,
    int ListingsAddedActive,
    int ListingsAddedSold,
    int ListingsUpdated,
    int ListingsSkipped,
    int ListingsFailed,
    int TotalListingsFound,
    int? TotalReportedBySearch,
    int BackfillItemPageFetches,
    DateTime StartedUtc,
    DateTime? SearchCompletedUtc,
    DateTime? DetailCompletedUtc,
    DateTime? CompletedUtc,
    IReadOnlyList<ScrapeRunIssueView> Issues);
