namespace MarketMakerEtl.Core.Models.Trades;

public sealed record TradeSale(
    DateTime SoldUtc,
    decimal SellPrice,
    decimal? SellShipping,
    decimal? SellFees,
    TradeStatus? Status,
    string? Notes);
