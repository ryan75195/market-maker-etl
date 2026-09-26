namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealSignalEvaluationResult(
    int SignalId,
    decimal? ForwardNetMedian,
    int ForwardSoldCount,
    decimal? RealisedMargin,
    double? ListingSoldWithinHours,
    bool UsedEstimatedDates,
    int EvaluationWindowDays);
