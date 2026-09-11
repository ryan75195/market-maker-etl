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
    string? PrimaryImageUrl);
