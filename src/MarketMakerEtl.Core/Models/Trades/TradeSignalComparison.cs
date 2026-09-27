namespace MarketMakerEtl.Core.Models.Trades;

public sealed record TradeSignalComparison(int EvaluatedCount, decimal MeanError, double BeatPredictionShare);
