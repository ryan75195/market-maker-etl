namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record DetailBacklogOptions(
    bool Enabled,
    int TickMinutes,
    int MaxFetchesPerTick,
    int MaxFetchesPerHour,
    int MaxDetailFetchAttempts,
    int FamilyDetailFetchesPerTick = 300,
    int MaxConcurrentDetailFetches = 1,
    bool FamilyInScopeOnly = true,
    int InfrastructureBackoffBaseSeconds = 1,
    int InfrastructureBackoffMaxSeconds = 1800);
