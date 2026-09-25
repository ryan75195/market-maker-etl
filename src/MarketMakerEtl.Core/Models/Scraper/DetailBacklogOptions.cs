namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record DetailBacklogOptions(
    bool Enabled,
    int TickMinutes,
    int MaxFetchesPerTick,
    int MaxFetchesPerHour,
    int MaxDetailFetchAttempts);
