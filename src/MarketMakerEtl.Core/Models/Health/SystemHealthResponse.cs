using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Models.Health;

public sealed record SystemHealthResponse(
    SystemHealthStatus Status,
    bool DatabaseReachable,
    IReadOnlyList<JobHealthView> Jobs,
    ClassifierHealthCheckResult Classifier,
    SystemHealthBacklogs Backlogs,
    IReadOnlyList<FamilyReviewView> Review);
