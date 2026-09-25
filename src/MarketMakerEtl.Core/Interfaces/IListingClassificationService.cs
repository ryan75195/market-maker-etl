using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IListingClassificationService
{
    Task<ClassificationTickResult> ClassifyPending(CancellationToken ct);
}
