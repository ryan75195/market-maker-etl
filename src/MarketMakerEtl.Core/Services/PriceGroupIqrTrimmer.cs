namespace MarketMakerEtl.Core.Services;

internal static class PriceGroupIqrTrimmer
{
    public const int MinCountForTrim = 8;

    public static PriceGroupIqrTrimResult Trim(IReadOnlyList<PriceGroupSoldObservation> observations)
    {
        if (observations.Count < MinCountForTrim)
        {
            return new PriceGroupIqrTrimResult(observations, 0);
        }

        var listedPrices = observations.Select(o => o.ListedPrice).ToList();
        var q1 = PriceGroupPercentileCalculator.Percentile(listedPrices, 0.25)!.Value;
        var q3 = PriceGroupPercentileCalculator.Percentile(listedPrices, 0.75)!.Value;
        var iqr = q3 - q1;
        var lowerBound = q1 - (1.5m * iqr);
        var upperBound = q3 + (1.5m * iqr);

        var kept = observations
            .Where(o => o.ListedPrice >= lowerBound && o.ListedPrice <= upperBound)
            .ToList();

        return new PriceGroupIqrTrimResult(kept, observations.Count - kept.Count);
    }
}
