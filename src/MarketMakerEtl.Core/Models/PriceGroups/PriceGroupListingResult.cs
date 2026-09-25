namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupListingResult(
    int ListingId,
    string? Title,
    string? Url,
    decimal? Price,
    DateTime? SoldDate,
    bool SoldDateIsEstimated,
    decimal? DeltaFromSoldMedian);
