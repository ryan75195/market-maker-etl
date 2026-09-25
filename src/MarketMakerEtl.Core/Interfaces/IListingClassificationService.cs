using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IListingClassificationService
{
    Task<ClassificationTickResult> ClassifyPending(Action<ClassificationBatchFailure> onBatchFailure, CancellationToken ct);
}
