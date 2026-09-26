namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupForwardWindowResult(int SoldCount, decimal? NetMedian, bool UsedEstimatedDates);
