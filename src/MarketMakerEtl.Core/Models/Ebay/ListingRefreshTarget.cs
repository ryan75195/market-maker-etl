using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record ListingRefreshTarget(
    int Id,
    string ListingId,
    string? Url,
    string? ItemStatus,
    Marketplace Marketplace = Marketplace.Ebay);
