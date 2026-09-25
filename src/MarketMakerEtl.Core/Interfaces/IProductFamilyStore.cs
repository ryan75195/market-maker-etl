using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Interfaces;

public interface IProductFamilyStore
{
    Task<ProductFamilyView> CreateFamily(string key, string name, string modelName, CancellationToken ct);

    Task<IReadOnlyList<ProductFamilyView>> GetFamilies(CancellationToken ct);

    Task<ProductFamilyView?> GetFamily(int familyId, CancellationToken ct);

    Task<TaxonomyVersionView?> AddTaxonomyVersion(int familyId, string questionsJson, CancellationToken ct);

    Task<TaxonomyVersionView?> GetTaxonomyVersion(int familyId, int version, CancellationToken ct);

    Task<TaxonomyVersionView?> GetLatestTaxonomyVersion(int familyId, CancellationToken ct);

    Task<bool> SetJobFamily(int jobId, int? productFamilyId, CancellationToken ct);
}
