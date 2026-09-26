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
    DateTime CreatedUtc,
    decimal? ForwardNetMedian = null,
    int? ForwardSoldCount = null,
    decimal? RealisedMargin = null,
    double? ListingSoldWithinHours = null,
    bool UsedEstimatedDates = false);
