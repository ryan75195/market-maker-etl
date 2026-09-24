namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record DetailFetchOptions(int MaxConcurrentDetailFetches, int MaxDetailFetchesPerRun);
