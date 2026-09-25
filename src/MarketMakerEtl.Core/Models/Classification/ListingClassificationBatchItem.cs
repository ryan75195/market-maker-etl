namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ListingClassificationBatchItem(
    int ListingEntityId,
    int TaxonomyVersionId,
    IReadOnlyList<ListingClassificationRow> Rows);
