namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealPerformanceSummary(
    int EvaluatedCount,
    double? PositiveMarginShare,
    decimal? MedianMargin,
    double? MedianListingSoldWithinHours);
