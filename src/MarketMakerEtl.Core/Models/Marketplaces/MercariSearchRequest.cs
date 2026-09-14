namespace MarketMakerEtl.Core.Models.Marketplaces;

public sealed record MercariSearchRequest(
    string SearchTerm,
    bool Sold,
    string? BrandId = null,
    string? CategoryId = null,
    string? Condition = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null);
