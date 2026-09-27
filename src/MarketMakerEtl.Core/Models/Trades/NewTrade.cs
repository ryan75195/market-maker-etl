namespace MarketMakerEtl.Core.Models.Trades;

public sealed record NewTrade(
    int? DealSignalId,
    int? ListingEntityId,
    int? ProductFamilyId,
    IReadOnlyDictionary<string, string>? PriceGroupKey,
    DateTime BoughtUtc,
    decimal BuyPrice,
    decimal? BuyShipping,
    decimal? BuyFees,
    string? Notes);
