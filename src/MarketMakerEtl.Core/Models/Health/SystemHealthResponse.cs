namespace MarketMakerEtl.Core.Models.Health;

public sealed record SystemHealthResponse(
    SystemHealthStatus Status,
    bool DatabaseReachable,
    IReadOnlyList<JobHealthView> Jobs,
    FetcherHealthView Fetcher,
    LlmHealthView Llm,
    SystemHealthBacklogs Backlogs,
    IReadOnlyList<FamilyReviewView> Review);
