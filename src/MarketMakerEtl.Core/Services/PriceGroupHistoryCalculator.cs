using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

internal static class PriceGroupHistoryCalculator
{
    public static IReadOnlyList<PriceGroupHistoryBucket> Build(
        IReadOnlyList<PriceGroupListingCandidate> candidates,
        PriceGroupHistoryQuery query,
        DateTime nowUtc,
        PriceGroupOptions options)
    {
        var bucketStarts = BuildBucketStarts(nowUtc, query.Bucket, query.Weeks);
        var observationsByBucket = bucketStarts.ToDictionary(
            start => start, _ => new List<PriceGroupSoldObservation>());

        foreach (var candidate in candidates)
        {
            AddCandidate(candidate, query, options, observationsByBucket);
        }

        return bucketStarts
            .Select(start => Summarize(start, observationsByBucket[start], query))
            .ToList();
    }

    private static void AddCandidate(
        PriceGroupListingCandidate candidate,
        PriceGroupHistoryQuery query,
        PriceGroupOptions options,
        Dictionary<DateTime, List<PriceGroupSoldObservation>> observationsByBucket)
    {
        var soldDate = ResolveSoldDate(candidate, query.IncludeEstimatedDates);
        if (soldDate is null)
        {
            return;
        }

        var bucketStart = BucketStartFor(soldDate.Value, query.Bucket);
        if (!observationsByBucket.TryGetValue(bucketStart, out var observations))
        {
            return;
        }

        var netProceeds = PriceGroupNetCalculator.ComputeNetProceeds(
            candidate.SoldPrice!.Value, candidate.ShippingPayer, candidate.ShippingCost, options);
        observations.Add(new PriceGroupSoldObservation(candidate.SoldPrice.Value, netProceeds));
    }

    private static DateTime? ResolveSoldDate(PriceGroupListingCandidate candidate, bool includeEstimatedDates)
    {
        if (!candidate.IsSold || !candidate.SoldPrice.HasValue)
        {
            return null;
        }

        return includeEstimatedDates ? candidate.EffectiveSoldDate : candidate.SoldDate;
    }

    private static PriceGroupHistoryBucket Summarize(
        DateTime bucketStart, List<PriceGroupSoldObservation> observations, PriceGroupHistoryQuery query)
    {
        var trimResult = query.TrimIqr
            ? PriceGroupIqrTrimmer.Trim(observations)
            : new PriceGroupIqrTrimResult(observations, 0);

        var values = trimResult.Kept
            .Select(o => query.Basis == PriceGroupHistoryBasis.Net ? o.NetProceeds : o.ListedPrice)
            .ToList();

        return new PriceGroupHistoryBucket(
            bucketStart,
            values.Count,
            PriceGroupPercentileCalculator.Percentile(values, 0.5),
            PriceGroupPercentileCalculator.Percentile(values, 0.25),
            PriceGroupPercentileCalculator.Percentile(values, 0.75));
    }

    private static List<DateTime> BuildBucketStarts(
        DateTime nowUtc, PriceGroupHistoryBucketGranularity bucket, int weeks)
    {
        var stepDays = bucket == PriceGroupHistoryBucketGranularity.Week ? 7 : 1;
        var bucketCount = bucket == PriceGroupHistoryBucketGranularity.Week ? weeks : weeks * 7;
        var latestBucketStart = BucketStartFor(nowUtc, bucket);

        var starts = new List<DateTime>(bucketCount);
        for (var i = bucketCount - 1; i >= 0; i--)
        {
            starts.Add(latestBucketStart.AddDays(-stepDays * i));
        }

        return starts;
    }

    private static DateTime BucketStartFor(DateTime instant, PriceGroupHistoryBucketGranularity bucket)
    {
        var date = instant.Date;
        if (bucket == PriceGroupHistoryBucketGranularity.Day)
        {
            return date;
        }

        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
