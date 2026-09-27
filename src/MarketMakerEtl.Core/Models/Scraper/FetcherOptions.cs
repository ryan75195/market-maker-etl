namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record FetcherOptions(string BaseUrl, TimeSpan Timeout);
