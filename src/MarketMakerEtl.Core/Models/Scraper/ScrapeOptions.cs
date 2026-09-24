namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeOptions(
    int MaxPages,
    bool CollectSold,
    int MaxBandsPerDirection = 200,
    int SoldBackfillDays = 30);
