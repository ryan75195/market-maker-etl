namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealSignalView(
    int Id,
    int ListingEntityId,
    string? Title,
    string? Url,
    int ProductFamilyId,
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> GroupKey,
    decimal LandedPrice,
    decimal? SoldNetMedian,
    int SoldCount,
    decimal? SoldP25,
    decimal Discount,
    DateTime CreatedUtc);
