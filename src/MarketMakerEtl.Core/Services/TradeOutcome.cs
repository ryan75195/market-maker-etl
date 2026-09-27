namespace MarketMakerEtl.Core.Services;

internal sealed record TradeOutcome(decimal RealisedProfit, double? DaysToSell, decimal? PredictedMargin);
