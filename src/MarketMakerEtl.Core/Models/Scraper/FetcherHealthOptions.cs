namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record FetcherHealthOptions(int RecentWindowMinutes, int MinRecentAttemptsForDegraded);
