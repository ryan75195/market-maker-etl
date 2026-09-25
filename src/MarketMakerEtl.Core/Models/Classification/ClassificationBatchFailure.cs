namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationBatchFailure(int JobId, int ListingCount, string ErrorMessage);
