using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ClassificationReviewStore : IClassificationReviewStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ClassificationReviewStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ClassificationReviewRow>> GetReviewQueue(
        int productFamilyId, int latestTaxonomyVersionId, IReadOnlyList<string>? questions, double threshold, int take, CancellationToken ct)
    {
        if (take <= 0)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var candidates = await BuildModelCandidateQuery(db, productFamilyId, latestTaxonomyVersionId, questions).ToListAsync(ct);

        return candidates
            .Where(x => ClassificationReviewPolicy.NeedsReview(x.Classification, threshold))
            .OrderBy(x => x.Classification.Confidence)
            .Take(take)
            .Select(x => MapToReviewRow(x.Classification, x.Listing))
            .ToList();
    }

    public async Task<IReadOnlyList<ClassificationReviewCount>> GetReviewSummary(
        int productFamilyId, int latestTaxonomyVersionId, double threshold, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var candidates = await BuildModelCandidateQuery(db, productFamilyId, latestTaxonomyVersionId, questions: null).ToListAsync(ct);

        return candidates
            .Select(x => x.Classification)
            .Where(c => ClassificationReviewPolicy.NeedsReview(c, threshold))
            .GroupBy(c => c.Question, StringComparer.Ordinal)
            .Select(g => new ClassificationReviewCount(g.Key, g.Count()))
            .ToList();
    }

    public async Task<int?> GetTaxonomyVersionId(int listingEntityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ListingClassifications
            .Where(c => c.ListingEntityId == listingEntityId)
            .Select(c => (int?)c.TaxonomyVersionId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ListingClassificationView?> SetHumanAnswer(
        int listingEntityId, string question, string choice, TaxonomyDocument taxonomy, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ListingClassifications
            .Where(c => c.ListingEntityId == listingEntityId)
            .ToListAsync(ct);
        var rowsByQuestion = rows.ToDictionary(r => r.Question, StringComparer.Ordinal);

        if (!rowsByQuestion.TryGetValue(question, out var targetRow))
        {
            return null;
        }

        ApplyHumanAnswer(taxonomy, rowsByQuestion, question, choice);
        await SqliteBusyRetry.ExecuteAsync(() => db.SaveChangesAsync(ct), ct);

        var version = await db.TaxonomyVersions
            .Where(v => v.Id == targetRow.TaxonomyVersionId)
            .Select(v => v.Version)
            .FirstOrDefaultAsync(ct);

        return new ListingClassificationView(
            listingEntityId, version, rows.Select(ListingClassificationStore.MapToAnswerView).ToList());
    }

    public async Task<IReadOnlyList<ClassificationExportListing>> GetLabelExportRows(
        int productFamilyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listingIds = await (
            from c in db.ListingClassifications
            join l in db.Listings on c.ListingEntityId equals l.Id
            join j in db.ScrapeJobs on l.ScrapeJobId equals j.Id
            where j.ProductFamilyId == productFamilyId && c.Source == ClassificationSource.Human
            select l.Id)
            .Distinct()
            .ToListAsync(ct);

        if (listingIds.Count == 0)
        {
            return [];
        }

        var listings = await db.Listings.Where(l => listingIds.Contains(l.Id)).ToListAsync(ct);
        var rows = await db.ListingClassifications
            .Where(c => listingIds.Contains(c.ListingEntityId))
            .ToListAsync(ct);
        var versionNumbersById = await db.TaxonomyVersions
            .Where(v => v.ProductFamilyId == productFamilyId)
            .ToDictionaryAsync(v => v.Id, v => v.Version, ct);

        return BuildExportListings(listingIds, listings, rows, versionNumbersById);
    }

    private static IQueryable<ClassificationCandidate> BuildModelCandidateQuery(
        EtlDbContext db, int productFamilyId, int latestTaxonomyVersionId, IReadOnlyList<string>? questions) =>
        from c in db.ListingClassifications
        join l in db.Listings on c.ListingEntityId equals l.Id
        join j in db.ScrapeJobs on l.ScrapeJobId equals j.Id
        where j.ProductFamilyId == productFamilyId
            && c.Source == ClassificationSource.Model
            && c.TaxonomyVersionId == latestTaxonomyVersionId
            && (questions == null || questions.Contains(c.Question))
        select new ClassificationCandidate(c, l);

    private static void ApplyHumanAnswer(
        TaxonomyDocument taxonomy,
        Dictionary<string, ListingClassificationEntity> rowsByQuestion,
        string question,
        string choice)
    {
        var modelChoices = rowsByQuestion.ToDictionary(
            kv => kv.Key, kv => kv.Value.Choice, StringComparer.Ordinal);
        var humanChoices = rowsByQuestion.Values
            .Where(r => r.Source == ClassificationSource.Human)
            .ToDictionary(r => r.Question, r => r.Choice, StringComparer.Ordinal);
        humanChoices[question] = choice;

        var resolved = TaxonomyAnswerResolver.Resolve(taxonomy, modelChoices, humanChoices);
        var now = DateTime.UtcNow;

        foreach (var answer in resolved)
        {
            ApplyResolvedAnswer(rowsByQuestion, answer, question, choice, now);
        }
    }

    private static void ApplyResolvedAnswer(
        Dictionary<string, ListingClassificationEntity> rowsByQuestion,
        ResolvedTaxonomyAnswer answer,
        string question,
        string choice,
        DateTime now)
    {
        if (!rowsByQuestion.TryGetValue(answer.Question, out var row))
        {
            return;
        }

        row.IsApplicable = answer.IsApplicable;
        row.ResolvedChoice = answer.ResolvedChoice;

        if (answer.Question != question)
        {
            return;
        }

        row.Choice = choice;
        row.Source = ClassificationSource.Human;
        row.Confidence = 1;
        row.Agreement = 1;
        row.ClassifiedUtc = now;
    }

    private static IReadOnlyList<ClassificationExportListing> BuildExportListings(
        IReadOnlyList<int> listingIds,
        List<ListingEntity> listings,
        List<ListingClassificationEntity> rows,
        Dictionary<int, int> versionNumbersById)
    {
        var rowsByListing = rows.GroupBy(r => r.ListingEntityId).ToDictionary(g => g.Key, g => g.ToList());
        var listingsById = listings.ToDictionary(l => l.Id);
        var result = new List<ClassificationExportListing>(listingIds.Count);

        foreach (var listingId in listingIds)
        {
            var listing = listingsById[listingId];
            var listingRows = rowsByListing[listingId];
            var version = versionNumbersById.GetValueOrDefault(listingRows[0].TaxonomyVersionId);
            result.Add(new ClassificationExportListing(
                listing.ListingId,
                version,
                ListingClassificationStore.MapToTarget(listing),
                listingRows.Select(ListingClassificationStore.MapToAnswerView).ToList()));
        }

        return result;
    }

    private static ClassificationReviewRow MapToReviewRow(ListingClassificationEntity classification, ListingEntity listing) =>
        new(
            listing.Id,
            listing.Title,
            listing.Url,
            listing.Price,
            listing.IsSold,
            listing.PrimaryImageUrl,
            listing.ImageUrls,
            listing.Description,
            listing.Condition,
            listing.Category0Name,
            listing.Category1Name,
            listing.Category2Name,
            classification.Question,
            classification.Choice,
            classification.Confidence,
            classification.Agreement,
            classification.ProbabilitiesJson);

    private sealed record ClassificationCandidate(ListingClassificationEntity Classification, ListingEntity Listing);
}
