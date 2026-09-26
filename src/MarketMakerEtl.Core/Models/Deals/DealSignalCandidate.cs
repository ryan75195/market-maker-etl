namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealSignalCandidate(
    int ListingEntityId,
    int ProductFamilyId,
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> GroupKey,
    decimal LandedPrice,
    decimal? SoldNetMedian,
    int SoldCount,
    decimal? SoldP25,
    decimal Discount);
