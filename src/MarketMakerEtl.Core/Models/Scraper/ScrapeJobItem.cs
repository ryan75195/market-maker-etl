namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeJobItem(
    string? PartitionKey,
    string? Url,
    string? BlobUri,
    string? Error);
