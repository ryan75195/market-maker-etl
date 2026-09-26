using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IListingClassifierClient
{
    Task<ClassifyResponse> Classify(ClassifyRequest request, CancellationToken ct);

    Task<ClassifierHealthCheckResult> CheckHealth(CancellationToken ct);
}
