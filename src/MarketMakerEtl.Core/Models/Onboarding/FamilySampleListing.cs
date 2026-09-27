namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record FamilySampleListing(
    string Id,
    string? Title,
    string? Description,
    string? Category,
    string? Brand,
    bool Sold,
    decimal? Price,
    string? Url);
