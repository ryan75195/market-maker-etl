namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeOptions(int MaxPages, bool CollectSold);
