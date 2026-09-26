using System.Text.Json;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class DealSignalStore : IDealSignalStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public DealSignalStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<bool> TryInsertSignal(DealSignalCandidate candidate, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var alreadyExists = await db.DealSignals.AnyAsync(
            s => s.ListingEntityId == candidate.ListingEntityId && s.LandedPrice == candidate.LandedPrice, ct);
        if (alreadyExists)
        {
            return false;
        }

        db.DealSignals.Add(BuildEntity(candidate));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<DealSignalView>> GetSignals(
        int productFamilyId, DateTime? since, int take, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await LoadRows(db, productFamilyId, since, take, ct);
        var listingIds = rows.Select(r => r.ListingEntityId).Distinct().ToList();
        var listings = await db.Listings.Where(l => listingIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        return rows.Select(r => MapToView(r, listings.GetValueOrDefault(r.ListingEntityId))).ToList();
    }

    private static Task<List<DealSignalEntity>> LoadRows(
        EtlDbContext db, int productFamilyId, DateTime? since, int take, CancellationToken ct)
    {
        var query = db.DealSignals.Where(s => s.ProductFamilyId == productFamilyId);
        if (since is DateTime sinceValue)
        {
            query = query.Where(s => s.CreatedUtc >= sinceValue);
        }

        return query.OrderByDescending(s => s.CreatedUtc).Take(take).ToListAsync(ct);
    }

    private static DealSignalEntity BuildEntity(DealSignalCandidate candidate) => new()
    {
        ListingEntityId = candidate.ListingEntityId,
        ProductFamilyId = candidate.ProductFamilyId,
        TaxonomyVersionId = candidate.TaxonomyVersionId,
        GroupKeyJson = JsonSerializer.Serialize(candidate.GroupKey),
        LandedPrice = candidate.LandedPrice,
        SoldNetMedian = candidate.SoldNetMedian,
        SoldCount = candidate.SoldCount,
        SoldP25 = candidate.SoldP25,
        Discount = candidate.Discount,
        CreatedUtc = DateTime.UtcNow
    };

    private static DealSignalView MapToView(DealSignalEntity entity, ListingEntity? listing)
    {
        var groupKey = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.GroupKeyJson)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new(
            entity.Id,
            entity.ListingEntityId,
            listing?.Title,
            listing?.Url,
            entity.ProductFamilyId,
            entity.TaxonomyVersionId,
            groupKey,
            entity.LandedPrice,
            entity.SoldNetMedian,
            entity.SoldCount,
            entity.SoldP25,
            entity.Discount,
            entity.CreatedUtc);
    }
}
