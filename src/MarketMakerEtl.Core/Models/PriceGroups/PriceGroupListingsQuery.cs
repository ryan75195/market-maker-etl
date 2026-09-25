namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupListingsQuery(
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> Where,
    PriceGroupListingStatus Status,
    int Take,
    int SoldDays,
    bool IncludeUncertain);
