namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealPerformanceBucket(string Range, DealPerformanceSummary Summary);
