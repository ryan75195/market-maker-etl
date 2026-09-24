namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ListingUpsertSummary(
    int AddedActive,
    int AddedSold,
    int Updated,
    int Skipped);
