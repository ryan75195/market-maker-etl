using MarketMakerEtl.Core.Models.Trades;

namespace MarketMakerEtl.Core.Services;

internal static class TradeSummaryCalculator
{
    public static TradeSummary Build(IReadOnlyList<TradeOutcome> outcomes)
    {
        var profits = outcomes.Select(o => o.RealisedProfit).ToList();
        var days = outcomes.Where(o => o.DaysToSell.HasValue).Select(o => o.DaysToSell!.Value).ToList();

        return new TradeSummary(
            outcomes.Count,
            profits.Sum(),
            PriceGroupPercentileCalculator.Percentile(profits, 0.5),
            MedianOfDays(days),
            BuildComparison(outcomes));
    }

    private static TradeSignalComparison? BuildComparison(IReadOnlyList<TradeOutcome> outcomes)
    {
        var withPrediction = outcomes.Where(o => o.PredictedMargin.HasValue).ToList();
        if (withPrediction.Count == 0)
        {
            return null;
        }

        var errors = withPrediction.Select(o => o.RealisedProfit - o.PredictedMargin!.Value).ToList();
        var beatCount = withPrediction.Count(o => o.RealisedProfit > o.PredictedMargin!.Value);

        return new TradeSignalComparison(
            withPrediction.Count,
            errors.Average(),
            (double)beatCount / withPrediction.Count);
    }

    private static double? MedianOfDays(IReadOnlyList<double> days)
    {
        if (days.Count == 0)
        {
            return null;
        }

        var sorted = days.OrderBy(v => v).ToList();
        var midIndex = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[midIndex - 1] + sorted[midIndex]) / 2.0 : sorted[midIndex];
    }
}
