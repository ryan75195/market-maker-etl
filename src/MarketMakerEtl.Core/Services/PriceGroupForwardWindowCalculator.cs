using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

internal static class PriceGroupForwardWindowCalculator
{
    public static PriceGroupForwardWindowResult Build(
        IReadOnlyList<PriceGroupListingCandidate> candidates,
        PriceGroupForwardWindowQuery query,
        PriceGroupOptions options)
    {
        var observations = new List<PriceGroupSoldObservation>();
        var usedEstimatedDates = false;

        foreach (var candidate in candidates)
        {
            if (!IsSoldWithinWindow(candidate, query))
            {
                continue;
            }

            observations.Add(new PriceGroupSoldObservation(
                candidate.SoldPrice!.Value,
                PriceGroupNetCalculator.ComputeNetProceeds(
                    candidate.SoldPrice.Value, candidate.ShippingPayer, candidate.ShippingCost, options)));

            if (candidate.SoldDate is null)
            {
                usedEstimatedDates = true;
            }
        }

        var trimResult = query.TrimIqr
            ? PriceGroupIqrTrimmer.Trim(observations)
            : new PriceGroupIqrTrimResult(observations, 0);
        var netProceeds = trimResult.Kept.Select(o => o.NetProceeds).ToList();

        return new PriceGroupForwardWindowResult(
            netProceeds.Count,
            PriceGroupPercentileCalculator.Percentile(netProceeds, 0.5),
            usedEstimatedDates);
    }

    private static bool IsSoldWithinWindow(PriceGroupListingCandidate candidate, PriceGroupForwardWindowQuery query) =>
        candidate.IsSold
        && candidate.SoldPrice.HasValue
        && candidate.EffectiveSoldDate > query.WindowStartExclusiveUtc
        && candidate.EffectiveSoldDate <= query.WindowEndInclusiveUtc;
}
