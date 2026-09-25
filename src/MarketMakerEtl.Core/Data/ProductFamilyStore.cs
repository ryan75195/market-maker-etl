using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ProductFamilyStore : IProductFamilyStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ProductFamilyStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<ProductFamilyView?> CreateFamily(string key, string name, string modelName, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var keyAlreadyExists = await db.ProductFamilies.AnyAsync(f => f.Key == key, ct);
        if (keyAlreadyExists)
        {
            return null;
        }

        var family = new ProductFamilyEntity
        {
            Key = key,
            Name = name,
            ModelName = modelName,
            CreatedUtc = DateTime.UtcNow
        };
        db.ProductFamilies.Add(family);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return null;
        }

        return MapToView(family, null);
    }

    public async Task<IReadOnlyList<ProductFamilyView>> GetFamilies(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var families = await db.ProductFamilies.OrderBy(f => f.Id).ToListAsync(ct);
        var views = new List<ProductFamilyView>(families.Count);
        foreach (var family in families)
        {
            var latest = await LoadLatestVersion(db, family.Id, ct);
            views.Add(MapToView(family, latest));
        }

        return views;
    }

    public async Task<ProductFamilyView?> GetFamily(int familyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var family = await db.ProductFamilies.FindAsync([familyId], ct);
        if (family is null)
        {
            return null;
        }

        var latest = await LoadLatestVersion(db, familyId, ct);
        return MapToView(family, latest);
    }

    public async Task<TaxonomyVersionView?> AddTaxonomyVersion(int familyId, string questionsJson, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var familyExists = await db.ProductFamilies.AnyAsync(f => f.Id == familyId, ct);
        if (!familyExists)
        {
            return null;
        }

        var version = new TaxonomyVersionEntity
        {
            ProductFamilyId = familyId,
            Version = await NextVersionNumber(db, familyId, ct),
            QuestionsJson = questionsJson,
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return MapToVersionView(version);
    }

    public async Task<TaxonomyVersionView?> GetTaxonomyVersion(int familyId, int version, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.TaxonomyVersions
            .FirstOrDefaultAsync(v => v.ProductFamilyId == familyId && v.Version == version, ct);
        return entity is null ? null : MapToVersionView(entity);
    }

    public async Task<TaxonomyVersionView?> GetLatestTaxonomyVersion(int familyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadLatestVersion(db, familyId, ct);
    }

    public async Task<bool> SetJobFamily(int jobId, int? productFamilyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return false;
        }

        job.ProductFamilyId = productFamilyId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<TaxonomyVersionView?> LoadLatestVersion(EtlDbContext db, int familyId, CancellationToken ct)
    {
        var entity = await db.TaxonomyVersions
            .Where(v => v.ProductFamilyId == familyId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : MapToVersionView(entity);
    }

    private static async Task<int> NextVersionNumber(EtlDbContext db, int familyId, CancellationToken ct)
    {
        var maxVersion = await db.TaxonomyVersions
            .Where(v => v.ProductFamilyId == familyId)
            .Select(v => (int?)v.Version)
            .MaxAsync(ct);
        return (maxVersion ?? 0) + 1;
    }

    private static ProductFamilyView MapToView(ProductFamilyEntity family, TaxonomyVersionView? latest) =>
        new(family.Id, family.Key, family.Name, family.ModelName, family.CreatedUtc, latest);

    private static TaxonomyVersionView MapToVersionView(TaxonomyVersionEntity version) =>
        new(version.Id, version.ProductFamilyId, version.Version, version.QuestionsJson, version.CreatedUtc);
}
