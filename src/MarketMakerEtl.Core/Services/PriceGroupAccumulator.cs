using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

internal sealed class PriceGroupAccumulator
{
    private readonly List<PriceGroupSoldObservation> _soldObservations = [];
    private readonly List<decimal> _activePrices = [];
    private readonly List<decimal> _activeLandedPrices = [];
    private readonly PriceGroupOptions _options;
    private int _shippingUnknownCount;
    private string? _currency;

    public PriceGroupAccumulator(IReadOnlyDictionary<string, string> key, PriceGroupOptions options)
    {
        Key = key;
        _options = options;
    }

    private IReadOnlyDictionary<string, string> Key { get; }

    public void Add(PriceGroupListingCandidate candidate, DateTime soldCutoff)
    {
        _currency ??= candidate.Currency;

        if (candidate.IsSold && candidate.SoldPrice.HasValue && candidate.EffectiveSoldDate >= soldCutoff)
        {
            AddSold(candidate, candidate.SoldPrice.Value);
        }
        else if (!candidate.IsSold && candidate.Price.HasValue)
        {
            AddActive(candidate, candidate.Price.Value);
        }
    }

    public PriceGroupSummary ToSummary(bool trimIqr)
    {
        var trimResult = trimIqr
            ? PriceGroupIqrTrimmer.Trim(_soldObservations)
            : new PriceGroupIqrTrimResult(_soldObservations, 0);
        var listedPrices = trimResult.Kept.Select(o => o.ListedPrice).ToList();
        var netProceeds = trimResult.Kept.Select(o => o.NetProceeds).ToList();

        return new PriceGroupSummary(
            Key,
            listedPrices.Count,
            PriceGroupPercentileCalculator.Percentile(listedPrices, 0.5),
            PriceGroupPercentileCalculator.Percentile(listedPrices, 0.25),
            PriceGroupPercentileCalculator.Percentile(listedPrices, 0.75),
            listedPrices.Count == 0 ? null : listedPrices.Min(),
            listedPrices.Count == 0 ? null : listedPrices.Max(),
            _activePrices.Count,
            PriceGroupPercentileCalculator.Percentile(_activePrices, 0.5),
            _activePrices.Count == 0 ? null : _activePrices.Min(),
            _currency,
            PriceGroupPercentileCalculator.Percentile(netProceeds, 0.5),
            PriceGroupPercentileCalculator.Percentile(netProceeds, 0.25),
            PriceGroupPercentileCalculator.Percentile(netProceeds, 0.75),
            PriceGroupPercentileCalculator.Percentile(_activeLandedPrices, 0.5),
            _activeLandedPrices.Count == 0 ? null : _activeLandedPrices.Min(),
            _shippingUnknownCount,
            trimResult.TrimmedCount);
    }

    private void AddSold(PriceGroupListingCandidate candidate, decimal soldPrice)
    {
        var netProceeds = PriceGroupNetCalculator.ComputeNetProceeds(
            soldPrice, candidate.ShippingPayer, candidate.ShippingCost, _options);
        _soldObservations.Add(new PriceGroupSoldObservation(soldPrice, netProceeds));
        CountShippingUnknown(candidate.ShippingPayer);
    }

    private void AddActive(PriceGroupListingCandidate candidate, decimal price)
    {
        _activePrices.Add(price);
        _activeLandedPrices.Add(
            PriceGroupNetCalculator.ComputeLandedPrice(price, candidate.ShippingPayer, candidate.ShippingCost));
        CountShippingUnknown(candidate.ShippingPayer);
    }

    private void CountShippingUnknown(string? shippingPayer)
    {
        if (PriceGroupNetCalculator.IsShippingPayerUnknown(shippingPayer))
        {
            _shippingUnknownCount++;
        }
    }
}
