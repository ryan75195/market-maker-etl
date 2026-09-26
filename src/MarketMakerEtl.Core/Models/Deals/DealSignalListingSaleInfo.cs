namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealSignalListingSaleInfo(bool IsSold, DateTime EffectiveSoldDateUtc, bool SoldDateIsEstimated);
