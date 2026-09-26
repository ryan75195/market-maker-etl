namespace MarketMakerEtl.Core.Models.Families;

public sealed record ProductFamilyView(
    int Id,
    string Key,
    string Name,
    string ModelName,
    DateTime CreatedUtc,
    TaxonomyVersionView? LatestTaxonomyVersion,
    string? DealGroupBy = null,
    decimal DealMinDiscount = 0.20m,
    int DealMinSold = 5);
