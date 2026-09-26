namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupHistoryQuery(
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> Where,
    PriceGroupHistoryBucketGranularity Bucket,
    int Weeks,
    PriceGroupHistoryBasis Basis,
    bool IncludeEstimatedDates,
    bool TrimIqr);
