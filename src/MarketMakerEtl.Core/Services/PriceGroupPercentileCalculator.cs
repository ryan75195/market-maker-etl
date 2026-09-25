namespace MarketMakerEtl.Core.Services;

public static class PriceGroupPercentileCalculator
{
    public static decimal? Percentile(IReadOnlyList<decimal> values, double percentile)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var rank = percentile * (sorted.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var weight = (decimal)(rank - lowerIndex);
        return sorted[lowerIndex] + (weight * (sorted[upperIndex] - sorted[lowerIndex]));
    }
}
