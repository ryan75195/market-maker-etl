namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifierOptions(
    int TickMinutes,
    int MaxListingsPerTick,
    int DegradedAfterFailedBatches,
    int FailureBackoffBaseMinutes,
    int FailureBackoffMaxMinutes);
