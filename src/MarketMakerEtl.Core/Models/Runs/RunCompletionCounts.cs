namespace MarketMakerEtl.Core.Models.Runs;

public sealed record RunCompletionCounts(
    int ListingsAddedActive,
    int ListingsAddedSold,
    int ListingsUpdated,
    int ListingsSkipped,
    int ListingsFailed,
    int TotalListingsFound,
    int? TotalReportedBySearch,
    DateTime SearchCompletedUtc,
    DateTime DetailCompletedUtc);
