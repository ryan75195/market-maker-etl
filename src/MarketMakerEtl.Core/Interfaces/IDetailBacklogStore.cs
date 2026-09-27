using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDetailBacklogStore
{
    Task<IReadOnlyList<ListingDetailTarget>> GetBacklogListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct);

    Task<IReadOnlyList<ListingDetailTarget>> GetFamilyBacklogListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct);

    Task<int> CountFamilyListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct);

    Task<IReadOnlyList<ListingDetailTarget>> GetFamilyInScopeListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct);

    Task<IReadOnlyList<ListingDetailTarget>> GetFamilyUnclassifiedListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct);

    Task<int> CountFamilyInScopeListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct);
}
