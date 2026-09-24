using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingDetailTarget(
    int Id,
    string ListingId,
    string? Url,
    string? ItemStatus,
    Marketplace Marketplace = Marketplace.Ebay);
