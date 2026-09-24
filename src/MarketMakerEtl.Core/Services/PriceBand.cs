namespace MarketMakerEtl.Core.Services;

internal readonly record struct PriceBand(decimal? MinPrice, decimal? MaxPrice)
{
    private const decimal AssumedCeiling = 100_000m;

    private const decimal OneCent = 0.01m;

    internal static PriceBand Unfiltered => new(null, null);

    internal static IReadOnlyList<PriceBand> SeedBands() =>
    [
        new PriceBand(0m, 5m),
        new PriceBand(5.01m, 10m),
        new PriceBand(10.01m, 20m),
        new PriceBand(20.01m, 50m),
        new PriceBand(50.01m, 100m),
        new PriceBand(100.01m, 200m),
        new PriceBand(200.01m, 500m),
        new PriceBand(500.01m, 1000m),
        new PriceBand(1000.01m, null),
    ];

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
