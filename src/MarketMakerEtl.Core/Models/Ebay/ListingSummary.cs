namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingSummary(
    string ListingId,
    string? Title,
    decimal? Price,
    string? Currency,
    string? Url,
    bool IsSold,
    string? Condition,
    string? PrimaryImageUrl,
    string? BuyingFormat,
    string? Brand = null,
    decimal? OriginalPrice = null,
    string? Category = null,
    int? Likes = null,
    IReadOnlyList<string>? ImageUrls = null);
