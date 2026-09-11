using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record ScrapeJobRequest(
    [property: JsonPropertyName("Urls")] IReadOnlyList<string> Urls,
    [property: JsonPropertyName("SessionReference")] string? SessionReference = null);
