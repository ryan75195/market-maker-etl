using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Interfaces;

public interface IPriceGroupListingStore
{
    Task<IReadOnlyList<PriceGroupListingCandidate>> GetCandidates(
        int taxonomyVersionId, IReadOnlyCollection<string> questions, CancellationToken ct);
}
