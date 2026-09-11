namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeJobRequest(
    IReadOnlyList<string> Urls,
    string? SessionReference = null);
