using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class PriceGroupListingStore : IPriceGroupListingStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;
    private readonly ClassificationReviewOptions _reviewOptions;

    public PriceGroupListingStore(IDbContextFactory<EtlDbContext> factory, ClassificationReviewOptions reviewOptions)
    {
        _factory = factory;
        _reviewOptions = reviewOptions;
    }

    public async Task<IReadOnlyList<PriceGroupListingCandidate>> GetCandidates(
        int taxonomyVersionId, IReadOnlyCollection<string> questions, CancellationToken ct)
    {
        if (questions.Count == 0)
        {
            return [];
        }

        var realQuestions = questions.Where(q => q != MercariConditionNormalizer.QuestionKey).ToList();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await LoadClassificationRows(db, taxonomyVersionId, realQuestions, ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var listingIds = rows.Select(r => r.ListingEntityId).Distinct().ToList();
        var listings = await db.Listings
            .Where(l => listingIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        return BuildCandidates(rows, listings, taxonomyVersionId);
    }

    private static Task<List<ListingClassificationEntity>> LoadClassificationRows(
        EtlDbContext db, int taxonomyVersionId, IReadOnlyCollection<string> realQuestions, CancellationToken ct) =>
        realQuestions.Count > 0
            ? db.ListingClassifications
                .Where(c => c.TaxonomyVersionId == taxonomyVersionId && realQuestions.Contains(c.Question))
                .ToListAsync(ct)
            : db.ListingClassifications
                .Where(c => c.TaxonomyVersionId == taxonomyVersionId)
                .ToListAsync(ct);

    private IReadOnlyList<PriceGroupListingCandidate> BuildCandidates(
        List<ListingClassificationEntity> rows, Dictionary<int, ListingEntity> listings, int taxonomyVersionId)
    {
        var candidates = new List<PriceGroupListingCandidate>();
        foreach (var group in rows.GroupBy(r => r.ListingEntityId))
        {
            if (!listings.TryGetValue(group.Key, out var listing))
            {
                continue;
            }

            candidates.Add(BuildCandidate(listing, group, taxonomyVersionId));
        }

        return candidates;
    }

    private PriceGroupListingCandidate BuildCandidate(
        ListingEntity listing, IEnumerable<ListingClassificationEntity> rows, int taxonomyVersionId)
    {
        var answers = rows.Select(BuildAnswer).ToList();
        answers.Add(BuildConditionAnswer(listing, taxonomyVersionId));

        return new(
            listing.Id,
            listing.Title,
            listing.Url,
            listing.Currency,
            listing.IsSold,
            listing.Price,
            listing.SoldPrice,
            listing.SoldDate,
            listing.SoldDate ?? listing.UpdatedUtc ?? listing.CreatedUtc,
            answers,
            listing.ShippingPayer,
            listing.ShippingCost);
    }

    private PriceGroupAnswer BuildAnswer(ListingClassificationEntity row) =>
        new(
            row.Question,
            row.ResolvedChoice,
            row.IsApplicable,
            ClassificationReviewPolicy.NeedsReview(row, _reviewOptions.ReviewThreshold),
            row.TaxonomyVersionId);

    private static PriceGroupAnswer BuildConditionAnswer(ListingEntity listing, int taxonomyVersionId)
    {
        var normalized = MercariConditionNormalizer.Normalize(listing.Condition);
        return new(MercariConditionNormalizer.QuestionKey, normalized, normalized is not null, false, taxonomyVersionId);
    }
}
