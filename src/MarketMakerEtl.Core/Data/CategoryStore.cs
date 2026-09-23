using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class CategoryStore : ICategoryStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public CategoryStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<CategoryView> CreateCategory(string name, bool isEnabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = new CategoryEntity { Name = name, IsEnabled = isEnabled, CreatedUtc = DateTime.UtcNow };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return MapToView(category);
    }

    public async Task<IReadOnlyList<CategoryView>> GetCategories(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var categories = await db.Categories.OrderBy(c => c.Id).ToListAsync(ct);
        return categories.Select(MapToView).ToList();
    }

    public async Task<CategoryView?> GetCategory(int categoryId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FindAsync([categoryId], ct);
        return category is null ? null : MapToView(category);
    }

    public async Task<CategoryView?> UpdateCategory(int categoryId, string name, bool isEnabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FindAsync([categoryId], ct);
        if (category is null)
        {
            return null;
        }

        category.Name = name;
        category.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
        return MapToView(category);
    }

    public async Task<bool> DeleteCategory(int categoryId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FindAsync([categoryId], ct);
        if (category is null)
        {
            return false;
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CategoryView?> SetCategoryEnabled(int categoryId, bool isEnabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FindAsync([categoryId], ct);
        if (category is null)
        {
            return null;
        }

        category.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
        return MapToView(category);
    }

    private static CategoryView MapToView(CategoryEntity category) =>
        new(category.Id, category.Name, category.IsEnabled, category.CreatedUtc);
}
