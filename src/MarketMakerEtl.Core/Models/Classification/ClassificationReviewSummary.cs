namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationReviewSummary(IReadOnlyList<ClassificationReviewCount> Questions, int Total);
