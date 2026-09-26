using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

internal static class DetailBacklogQueries
{
    public static async Task<IReadOnlyList<ListingDetailTarget>> GetGeneralListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        var idArray = jobIds.ToArray();
        var listings = await db.Listings
            .Where(l => idArray.Contains(l.ScrapeJobId) && l.DetailFetchedUtc == null && l.DetailFetchAttempts < maxAttempts)
            .OrderBy(l => l.DetailFetchAttempts > 0 ? 2 : l.IsSold ? 0 : 1)
            .ThenByDescending(l => l.PostedUtc)
            .ThenByDescending(l => l.CreatedUtc)
            .ThenBy(l => l.DetailFetchAttempts)
            .ThenBy(l => l.Id)
            .Take(limit)
            .ToListAsync(ct);

        return ToTargets(listings);
    }

    public static async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        var listings = await FamilyFilter(db, jobIds.ToArray(), maxAttempts)
            .OrderBy(l => l.IsSold && l.SoldDate == null ? 0 : 1)
            .ThenByDescending(l => l.PostedUtc)
            .ThenByDescending(l => l.CreatedUtc)
            .ThenBy(l => l.DetailFetchAttempts)
            .ThenBy(l => l.Id)
            .Take(limit)
            .ToListAsync(ct);

        return ToTargets(listings);
    }

    public static async Task<int> CountFamilyListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct) =>
        jobIds.Count == 0
            ? 0
            : await FamilyFilter(db, jobIds.ToArray(), maxAttempts).CountAsync(ct);

    private static IQueryable<ListingEntity> FamilyFilter(EtlDbContext db, int[] jobIds, int maxAttempts) =>
        db.Listings.Where(l =>
            jobIds.Contains(l.ScrapeJobId)
            && l.DetailFetchAttempts < maxAttempts
            && (l.DetailFetchedUtc == null || (l.IsSold && l.SoldDate == null)));

    private static IReadOnlyList<ListingDetailTarget> ToTargets(List<ListingEntity> listings) =>
        listings
            .Select(l => new ListingDetailTarget(l.Id, l.ListingId, l.Url, l.ItemStatus, l.Marketplace))
            .ToList();
}
