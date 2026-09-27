using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IClassificationThrottleService
{
    bool IsBackingOffFailures();

    bool IsProbingAfterFailures();

    int ResolveTickBudget();

    void ObserveTickResult(ClassificationTickResult result);
}
