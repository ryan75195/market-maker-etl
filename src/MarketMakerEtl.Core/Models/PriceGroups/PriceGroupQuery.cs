namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupQuery(
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> Where,
    IReadOnlyList<string> By,
    int SoldDays,
    bool IncludeUncertain,
    int MinSold);
