namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupHistoryBucket(
    DateTime BucketStart,
    int SoldCount,
    decimal? Median,
    decimal? P25,
    decimal? P75);
