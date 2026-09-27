namespace MarketMakerEtl.Core.Models.Trades;

public sealed record TradeSummary(
    int Count,
    decimal TotalRealisedProfit,
    decimal? MedianRealisedProfit,
    double? MedianDaysToSell,
    TradeSignalComparison? SignalComparison);
