namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationTickResult(
    int JobsProcessed,
    int ListingsSelected,
    int ListingsClassified,
    IReadOnlyList<ClassificationBatchFailure> Failures);
