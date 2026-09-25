using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IListingClassificationStore
{
    Task<IReadOnlyList<ListingClassificationTarget>> GetListingsNeedingClassification(
        int scrapeJobId, int latestTaxonomyVersionId, int limit, CancellationToken ct);

    Task UpsertBatch(IReadOnlyList<ListingClassificationBatchItem> batch, CancellationToken ct);

    Task<ListingClassificationView?> GetClassification(int listingEntityId, CancellationToken ct);

    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>> GetHumanChoices(
        IReadOnlyList<int> listingEntityIds, CancellationToken ct);
}
