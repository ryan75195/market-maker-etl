namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingStatusObservation(
    string Status,
    decimal? Price,
    decimal? SoldPrice,
    DateTime? SoldDate,
    string? Seller,
    bool IsSold);
