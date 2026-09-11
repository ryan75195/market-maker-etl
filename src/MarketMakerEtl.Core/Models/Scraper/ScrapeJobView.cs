namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeJobView(string? JobId, ScrapeJobStatus Status);
