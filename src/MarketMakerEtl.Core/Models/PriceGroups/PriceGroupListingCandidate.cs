namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupListingCandidate(
    int ListingId,
    string? Title,
    string? Url,
    string? Currency,
    bool IsSold,
    decimal? Price,
    decimal? SoldPrice,
    DateTime? SoldDate,
    IReadOnlyList<PriceGroupAnswer> Answers);
