using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Core.Interfaces;

public interface ICategoryStore
{
    Task<CategoryView> CreateCategory(string name, bool isEnabled, CancellationToken ct);

    Task<IReadOnlyList<CategoryView>> GetCategories(CancellationToken ct);

    Task<CategoryView?> GetCategory(int categoryId, CancellationToken ct);

    Task<CategoryView?> UpdateCategory(int categoryId, string name, bool isEnabled, CancellationToken ct);

    Task<bool> DeleteCategory(int categoryId, CancellationToken ct);

    Task<CategoryView?> SetCategoryEnabled(int categoryId, bool isEnabled, CancellationToken ct);
}
