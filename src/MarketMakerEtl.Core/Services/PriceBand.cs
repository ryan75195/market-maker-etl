namespace MarketMakerEtl.Core.Services;

internal readonly record struct PriceBand(decimal? MinPrice, decimal? MaxPrice)
{
    private const decimal AssumedCeiling = 100_000m;

    private const decimal OneCent = 0.01m;

    internal static PriceBand Unfiltered => new(null, null);

    internal bool CanSplit(decimal minimumWidth) =>
        MinPrice is null || MaxPrice is null || MaxPrice.Value - MinPrice.Value > minimumWidth;

    internal IReadOnlyList<PriceBand> Split()
    {
        var lowerBound = MinPrice ?? 0m;
        var upperBound = MaxPrice ?? AssumedCeiling;
        var midpoint = RoundDownToCent(lowerBound + ((upperBound - lowerBound) / 2m));

        return [new PriceBand(MinPrice, midpoint), new PriceBand(midpoint + OneCent, MaxPrice)];
    }

    private static decimal RoundDownToCent(decimal value) =>
        Math.Floor(value * 100m) / 100m;
}
