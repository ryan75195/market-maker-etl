using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilySampleFetchService
{
    Task<IReadOnlyList<FamilySampleListing>> FetchSample(string searchTerm, CancellationToken ct);
}
