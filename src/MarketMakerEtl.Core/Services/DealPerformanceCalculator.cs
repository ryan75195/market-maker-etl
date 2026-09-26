using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Services;

internal static class DealPerformanceCalculator
{
    private static readonly (string Range, decimal Min, decimal? Max)[] Buckets =
    [
        ("20-30", 0.20m, 0.30m),
        ("30-50", 0.30m, 0.50m),
        ("50+", 0.50m, null)
    ];

    public static DealPerformanceReport Build(IReadOnlyList<DealSignalOutcome> outcomes)
    {
        var overall = Summarize(outcomes);
        var buckets = Buckets
            .Select(b => new DealPerformanceBucket(b.Range, Summarize(FilterBucket(outcomes, b.Min, b.Max))))
            .ToList();

        return new DealPerformanceReport(overall, buckets);
    }

    private static IReadOnlyList<DealSignalOutcome> FilterBucket(
        IReadOnlyList<DealSignalOutcome> outcomes, decimal min, decimal? max) =>
        outcomes.Where(o => o.Discount >= min && (max is null || o.Discount < max)).ToList();

    private static DealPerformanceSummary Summarize(IReadOnlyList<DealSignalOutcome> outcomes)
    {
        var margins = outcomes.Where(o => o.RealisedMargin.HasValue).Select(o => o.RealisedMargin!.Value).ToList();
        var hours = outcomes.Where(o => o.ListingSoldWithinHours.HasValue).Select(o => o.ListingSoldWithinHours!.Value).ToList();
        var positiveShare = margins.Count == 0 ? (double?)null : (double)margins.Count(m => m > 0m) / margins.Count;

        return new DealPerformanceSummary(
            outcomes.Count,
            positiveShare,
            PriceGroupPercentileCalculator.Percentile(margins, 0.5),
            MedianOfHours(hours));
    }

    private static double? MedianOfHours(IReadOnlyList<double> hours)
    {
        if (hours.Count == 0)
        {
            return null;
        }

        var sorted = hours.OrderBy(v => v).ToList();
        var midIndex = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[midIndex - 1] + sorted[midIndex]) / 2.0 : sorted[midIndex];
    }
}
