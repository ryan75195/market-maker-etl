namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record MercariCategoryHierarchy(
    int? Level0Id = null,
    string? Level0Name = null,
    int? Level1Id = null,
    string? Level1Name = null,
    int? Level2Id = null,
    string? Level2Name = null);
