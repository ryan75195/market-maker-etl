namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupSummary(
    IReadOnlyDictionary<string, string> Key,
    int SoldCount,
    decimal? SoldMedian,
    decimal? SoldP25,
    decimal? SoldP75,
    decimal? SoldMin,
    decimal? SoldMax,
    int ActiveCount,
    decimal? ActiveMedianAsk,
    decimal? ActiveMinAsk,
    string? Currency);
