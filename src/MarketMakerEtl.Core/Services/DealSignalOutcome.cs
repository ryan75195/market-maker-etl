namespace MarketMakerEtl.Core.Services;

internal sealed record DealSignalOutcome(decimal Discount, decimal? RealisedMargin, double? ListingSoldWithinHours);
