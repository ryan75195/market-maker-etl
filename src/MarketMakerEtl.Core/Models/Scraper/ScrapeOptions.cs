namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeOptions(
    int MaxPages,
    bool CollectSold,
    int MaxBandsPerDirection = 200,
    int SoldBackfillDays = 30,
    int MaxBackfillItemPageFetches = 400,
    int SearchPageMaxAttempts = 5,
    int SearchPageRetryBaseDelaySeconds = 5,
    int SearchConcurrency = 3);
