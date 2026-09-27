using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
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

        var listings = await OrderFamilyListings(FamilyFilter(db, jobIds.ToArray(), maxAttempts))
            .Take(limit)
            .ToListAsync(ct);

        return ToTargets(listings);
    }

    public static async Task<int> CountFamilyListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct) =>
        jobIds.Count == 0
            ? 0
            : await FamilyFilter(db, jobIds.ToArray(), maxAttempts).CountAsync(ct);

    public static async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyInScopeListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        var listings = new List<ListingEntity>();
        foreach (var group in await LoadFamilyJobGroups(db, jobIds, ct))
        {
            if (listings.Count >= limit)
            {
                break;
            }

            var scope = await LoadFamilyScope(db, group.ProductFamilyId, ct);
            if (scope is null)
            {
                continue;
            }

            var query = ApplyInScopeFilter(FamilyFilter(db, group.JobIds.ToArray(), maxAttempts), db, scope);
            var batch = await OrderFamilyListings(query).Take(limit - listings.Count).ToListAsync(ct);
            listings.AddRange(batch);
        }

        return ToTargets(listings);
    }

    public static async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyUnclassifiedListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        var listings = new List<ListingEntity>();
        foreach (var group in await LoadFamilyJobGroups(db, jobIds, ct))
        {
            if (listings.Count >= limit)
            {
                break;
            }

            var query = await BuildFamilyUnclassifiedQuery(db, group, maxAttempts, ct);
            var batch = await OrderFamilyListings(query).Take(limit - listings.Count).ToListAsync(ct);
            listings.AddRange(batch);
        }

        return ToTargets(listings);
    }

    public static async Task<int> CountFamilyInScopeListingsNeedingDetail(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var group in await LoadFamilyJobGroups(db, jobIds, ct))
        {
            var scope = await LoadFamilyScope(db, group.ProductFamilyId, ct);
            if (scope is null)
            {
                continue;
            }

            var query = ApplyInScopeFilter(FamilyFilter(db, group.JobIds.ToArray(), maxAttempts), db, scope);
            total += await query.CountAsync(ct);
        }

        return total;
    }

    private static async Task<IQueryable<ListingEntity>> BuildFamilyUnclassifiedQuery(
        EtlDbContext db, FamilyJobGroup group, int maxAttempts, CancellationToken ct)
    {
        var baseQuery = FamilyFilter(db, group.JobIds.ToArray(), maxAttempts);
        var scope = await LoadFamilyScope(db, group.ProductFamilyId, ct);

        if (scope is null)
        {
            return baseQuery;
        }

        if (scope.GateOpeningChoices.Count == 0)
        {
            return baseQuery.Where(_ => false);
        }

        var fullyGatedIds = await ApplyAllGatesClassifiedFilter(baseQuery, db, scope)
            .Select(l => l.Id)
            .ToListAsync(ct);
        var fullyGatedIdSet = fullyGatedIds.ToHashSet();

        return baseQuery.Where(l => !fullyGatedIdSet.Contains(l.Id));
    }

    private static IQueryable<ListingEntity> ApplyInScopeFilter(
        IQueryable<ListingEntity> query, EtlDbContext db, FamilyGateScope scope)
    {
        foreach (var (question, allowedChoices) in scope.GateOpeningChoices)
        {
            query = query.Where(l => db.ListingClassifications.Any(c =>
                c.ListingEntityId == l.Id
                && c.TaxonomyVersionId == scope.TaxonomyVersionId
                && c.Question == question
                && c.ResolvedChoice != null
                && allowedChoices.Contains(c.ResolvedChoice)));
        }

        return query;
    }

    private static IQueryable<ListingEntity> ApplyAllGatesClassifiedFilter(
        IQueryable<ListingEntity> query, EtlDbContext db, FamilyGateScope scope)
    {
        foreach (var question in scope.GateOpeningChoices.Keys)
        {
            query = query.Where(l => db.ListingClassifications.Any(c =>
                c.ListingEntityId == l.Id
                && c.TaxonomyVersionId == scope.TaxonomyVersionId
                && c.Question == question));
        }

        return query;
    }

    private static async Task<IReadOnlyList<FamilyJobGroup>> LoadFamilyJobGroups(
        EtlDbContext db, IReadOnlyCollection<int> jobIds, CancellationToken ct)
    {
        var idArray = jobIds.ToArray();
        var jobFamilies = await db.ScrapeJobs
            .Where(j => idArray.Contains(j.Id) && j.ProductFamilyId != null)
            .Select(j => new { j.Id, ProductFamilyId = j.ProductFamilyId!.Value })
            .ToListAsync(ct);

        return jobFamilies
            .GroupBy(j => j.ProductFamilyId)
            .OrderBy(group => group.Key)
            .Select(group => new FamilyJobGroup(group.Key, group.Select(j => j.Id).ToList()))
            .ToList();
    }

    private static async Task<FamilyGateScope?> LoadFamilyScope(
        EtlDbContext db, int productFamilyId, CancellationToken ct)
    {
        var version = await db.TaxonomyVersions
            .Where(v => v.ProductFamilyId == productFamilyId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (version is null)
        {
            return null;
        }

        var document = TaxonomyDocumentParser.Parse(version.QuestionsJson);
        return new FamilyGateScope(version.Id, TaxonomyScopeAnalyzer.GetGateOpeningChoices(document));
    }

    private static IQueryable<ListingEntity> FamilyFilter(EtlDbContext db, int[] jobIds, int maxAttempts) =>
        db.Listings.Where(l =>
            jobIds.Contains(l.ScrapeJobId)
            && l.DetailFetchAttempts < maxAttempts
            && (l.DetailFetchedUtc == null || (l.IsSold && l.SoldDate == null)));

    private static IOrderedQueryable<ListingEntity> OrderFamilyListings(IQueryable<ListingEntity> query) =>
        query
            .OrderBy(l => l.IsSold && l.SoldDate == null ? 0 : 1)
            .ThenByDescending(l => l.PostedUtc)
            .ThenByDescending(l => l.CreatedUtc)
            .ThenBy(l => l.DetailFetchAttempts)
            .ThenBy(l => l.Id);

    private static IReadOnlyList<ListingDetailTarget> ToTargets(List<ListingEntity> listings) =>
        listings
            .Select(l => new ListingDetailTarget(l.Id, l.ListingId, l.Url, l.ItemStatus, l.Marketplace))
            .ToList();

    private sealed record FamilyJobGroup(int ProductFamilyId, IReadOnlyList<int> JobIds);

    private sealed record FamilyGateScope(
        int TaxonomyVersionId, IReadOnlyDictionary<string, IReadOnlySet<string>> GateOpeningChoices);
}
