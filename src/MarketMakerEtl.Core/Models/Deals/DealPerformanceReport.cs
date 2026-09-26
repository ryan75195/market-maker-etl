namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealPerformanceReport(DealPerformanceSummary Overall, IReadOnlyList<DealPerformanceBucket> Buckets);
