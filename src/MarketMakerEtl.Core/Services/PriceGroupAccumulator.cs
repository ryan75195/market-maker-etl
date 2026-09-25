using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

internal sealed class PriceGroupAccumulator
{
    private readonly List<decimal> _soldPrices = [];
    private readonly List<decimal> _activePrices = [];
    private string? _currency;

    public PriceGroupAccumulator(IReadOnlyDictionary<string, string> key)
    {
        Key = key;
    }

    private IReadOnlyDictionary<string, string> Key { get; }

    public void Add(PriceGroupListingCandidate candidate, DateTime soldCutoff)
    {
        _currency ??= candidate.Currency;

        if (candidate.IsSold && candidate.SoldPrice.HasValue && candidate.SoldDate >= soldCutoff)
        {
            _soldPrices.Add(candidate.SoldPrice.Value);
        }
        else if (!candidate.IsSold && candidate.Price.HasValue)
        {
            _activePrices.Add(candidate.Price.Value);
        }
    }

    public PriceGroupSummary ToSummary() =>
        new(
            Key,
            _soldPrices.Count,
            PriceGroupPercentileCalculator.Percentile(_soldPrices, 0.5),
            PriceGroupPercentileCalculator.Percentile(_soldPrices, 0.25),
            PriceGroupPercentileCalculator.Percentile(_soldPrices, 0.75),
            _soldPrices.Count == 0 ? null : _soldPrices.Min(),
            _soldPrices.Count == 0 ? null : _soldPrices.Max(),
            _activePrices.Count,
            PriceGroupPercentileCalculator.Percentile(_activePrices, 0.5),
            _activePrices.Count == 0 ? null : _activePrices.Min(),
            _currency);
}
