namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingSummary(
    string ListingId,
    string? Title,
    decimal? Price,
    string? Currency,
    string? Url,
    bool IsSold);
