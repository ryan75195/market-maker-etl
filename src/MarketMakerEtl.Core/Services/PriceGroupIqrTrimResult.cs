namespace MarketMakerEtl.Core.Services;

internal sealed record PriceGroupIqrTrimResult(IReadOnlyList<PriceGroupSoldObservation> Kept, int TrimmedCount);
