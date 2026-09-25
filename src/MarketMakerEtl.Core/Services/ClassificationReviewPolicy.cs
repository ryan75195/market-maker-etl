using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

public static class ClassificationReviewPolicy
{
    public static bool NeedsReview(ListingClassificationEntity row, double threshold) =>
        row.Source == ClassificationSource.Model && row.IsApplicable && row.Confidence < threshold;
}
