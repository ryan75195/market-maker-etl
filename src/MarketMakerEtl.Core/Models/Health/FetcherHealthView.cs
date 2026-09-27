namespace MarketMakerEtl.Core.Models.Health;

public sealed record FetcherHealthView(
    bool SidecarReachable,
    int RecentWindowMinutes,
    int RecentSuccessCount,
    int RecentInfrastructureFailureCount,
    int RecentNotFoundFailureCount,
    int RecentOtherFailureCount,
    bool IsDegraded);
