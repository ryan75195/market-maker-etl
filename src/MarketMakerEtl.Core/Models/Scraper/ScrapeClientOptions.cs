namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeClientOptions(
    string BaseUrl,
    string ApiKey,
    TimeSpan FetchTimeout,
    TimeSpan PollInterval,
    string? SessionReference = null);
