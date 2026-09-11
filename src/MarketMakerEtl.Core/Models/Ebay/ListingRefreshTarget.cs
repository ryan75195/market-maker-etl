namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingRefreshTarget(
    int Id,
    string ListingId,
    string? Url,
    string? ItemStatus);
