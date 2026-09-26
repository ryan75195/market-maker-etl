namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupForwardWindowQuery(
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> GroupKey,
    DateTime WindowStartExclusiveUtc,
    DateTime WindowEndInclusiveUtc,
    bool TrimIqr);
