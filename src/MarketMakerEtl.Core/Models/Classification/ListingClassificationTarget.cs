namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ListingClassificationTarget(
    int ListingEntityId,
    string? Title,
    string? Category0Name,
    string? Category1Name,
    string? Category2Name,
    string? Brand,
    string? Description);
