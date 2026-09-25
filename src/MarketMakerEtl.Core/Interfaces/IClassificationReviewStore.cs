using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Interfaces;

public interface IClassificationReviewStore
{
    Task<IReadOnlyList<ClassificationReviewRow>> GetReviewQueue(
        int productFamilyId, int latestTaxonomyVersionId, string? question, double threshold, int take, CancellationToken ct);

    Task<IReadOnlyList<ClassificationReviewCount>> GetReviewSummary(
        int productFamilyId, int latestTaxonomyVersionId, double threshold, CancellationToken ct);

    Task<int?> GetTaxonomyVersionId(int listingEntityId, CancellationToken ct);

    Task<ListingClassificationView?> SetHumanAnswer(
        int listingEntityId, string question, string choice, TaxonomyDocument taxonomy, CancellationToken ct);

    Task<IReadOnlyList<ClassificationExportListing>> GetLabelExportRows(int productFamilyId, CancellationToken ct);
}
