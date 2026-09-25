using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ListingClassificationStore : IListingClassificationStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ListingClassificationStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ListingClassificationTarget>> GetListingsNeedingClassification(
        int scrapeJobId, int latestTaxonomyVersionId, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var candidates = await db.Listings
            .Where(l => l.ScrapeJobId == scrapeJobId)
            .Select(l => new
            {
                Listing = l,
                ClassificationCount = db.ListingClassifications.Count(c => c.ListingEntityId == l.Id),
                HasStaleVersion = db.ListingClassifications.Any(c =>
                    c.ListingEntityId == l.Id &&
                    c.Source == ClassificationSource.Model &&
                    c.TaxonomyVersionId != latestTaxonomyVersionId),
                MaxClassifiedUtc = db.ListingClassifications
                    .Where(c => c.ListingEntityId == l.Id)
                    .Select(c => (DateTime?)c.ClassifiedUtc)
                    .Max()
            })
            .Where(x =>
                x.ClassificationCount == 0 ||
                x.HasStaleVersion ||
                (x.Listing.DetailFetchedUtc != null &&
                    (x.MaxClassifiedUtc == null || x.Listing.DetailFetchedUtc > x.MaxClassifiedUtc)))
            .OrderByDescending(x => x.Listing.IsSold)
            .ThenBy(x => x.Listing.Id)
            .Take(limit)
            .Select(x => x.Listing)
            .ToListAsync(ct);

        return candidates.Select(MapToTarget).ToList();
    }

    public async Task UpsertBatch(IReadOnlyList<ListingClassificationBatchItem> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var listingIds = batch.Select(item => item.ListingEntityId).ToArray();
        var existing = await db.ListingClassifications
            .Where(c => listingIds.Contains(c.ListingEntityId))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(c => (c.ListingEntityId, c.Question));

        var classifiedUtc = DateTime.UtcNow;
        foreach (var item in batch)
        {
            foreach (var row in item.Rows)
            {
                ApplyRow(db, existingByKey, item, row, classifiedUtc);
            }
        }

        await SqliteBusyRetry.ExecuteAsync(() => db.SaveChangesAsync(ct), ct);
    }

    public async Task<ListingClassificationView?> GetClassification(int listingEntityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ListingClassifications
            .Where(c => c.ListingEntityId == listingEntityId)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var version = await db.TaxonomyVersions
            .Where(v => v.Id == rows[0].TaxonomyVersionId)
            .Select(v => v.Version)
            .FirstOrDefaultAsync(ct);

        return new ListingClassificationView(listingEntityId, version, rows.Select(MapToAnswerView).ToList());
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>> GetHumanChoices(
        IReadOnlyList<int> listingEntityIds, CancellationToken ct)
    {
        if (listingEntityIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyDictionary<string, string>>();
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ListingClassifications
            .Where(c => listingEntityIds.Contains(c.ListingEntityId) && c.Source == ClassificationSource.Human)
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ListingEntityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g.ToDictionary(
                    r => r.Question, r => r.Choice, StringComparer.Ordinal));
    }

    private static void ApplyRow(
        EtlDbContext db,
        Dictionary<(int ListingEntityId, string Question), ListingClassificationEntity> existingByKey,
        ListingClassificationBatchItem item,
        ListingClassificationRow row,
        DateTime classifiedUtc)
    {
        if (existingByKey.TryGetValue((item.ListingEntityId, row.Question), out var entity))
        {
            if (entity.Source == ClassificationSource.Human)
            {
                return;
            }
        }
        else
        {
            entity = new ListingClassificationEntity
            {
                ListingEntityId = item.ListingEntityId,
                Question = row.Question
            };
            db.ListingClassifications.Add(entity);
        }

        entity.TaxonomyVersionId = item.TaxonomyVersionId;
        entity.Choice = row.Choice;
        entity.ResolvedChoice = row.ResolvedChoice;
        entity.IsApplicable = row.IsApplicable;
        entity.Confidence = row.Confidence;
        entity.Agreement = row.Agreement;
        entity.ProbabilitiesJson = row.ProbabilitiesJson;
        entity.Source = ClassificationSource.Model;
        entity.ClassifiedUtc = classifiedUtc;
    }

    private static ListingClassificationTarget MapToTarget(ListingEntity listing) =>
        new(listing.Id, listing.Title, listing.Category0Name, listing.Category1Name, listing.Category2Name, listing.Brand, listing.Description);

    private static ListingClassificationAnswerView MapToAnswerView(ListingClassificationEntity entity) =>
        new(entity.Question, entity.Choice, entity.ResolvedChoice, entity.IsApplicable, entity.Confidence, entity.Agreement, entity.Source);
}
