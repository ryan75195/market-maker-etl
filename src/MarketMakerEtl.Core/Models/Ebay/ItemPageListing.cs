namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ItemPageListing(
    string? ListingId,
    string? Title,
    decimal? Price,
    string? Currency,
    string? Condition,
    string? BuyingFormat,
    string? Status,
    decimal? SoldPrice,
    string? SoldDate,
    string? Seller,
    string? PrimaryImageUrl,
    string? Brand = null,
    string? Description = null,
    IReadOnlyList<string>? ImageUrls = null,
    decimal? ShippingCost = null,
    decimal? OriginalPrice = null,
    DateTimeOffset? PostedUtc = null,
    int? Likes = null);
