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

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ListingClassifications
            .Where(c => c.TaxonomyVersionId == taxonomyVersionId && questions.Contains(c.Question))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var listingIds = rows.Select(r => r.ListingEntityId).Distinct().ToList();
        var listings = await db.Listings
            .Where(l => listingIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        return BuildCandidates(rows, listings);
    }

    private IReadOnlyList<PriceGroupListingCandidate> BuildCandidates(
        List<ListingClassificationEntity> rows, Dictionary<int, ListingEntity> listings)
    {
        var candidates = new List<PriceGroupListingCandidate>();
        foreach (var group in rows.GroupBy(r => r.ListingEntityId))
        {
            if (!listings.TryGetValue(group.Key, out var listing))
            {
                continue;
            }

            candidates.Add(BuildCandidate(listing, group));
        }

        return candidates;
    }

    private PriceGroupListingCandidate BuildCandidate(
        ListingEntity listing, IEnumerable<ListingClassificationEntity> rows) =>
        new(
            listing.Id,
            listing.Title,
            listing.Url,
            listing.Currency,
            listing.IsSold,
            listing.Price,
            listing.SoldPrice,
            listing.SoldDate,
            listing.SoldDate ?? listing.UpdatedUtc ?? listing.CreatedUtc,
            rows.Select(BuildAnswer).ToList());

    private PriceGroupAnswer BuildAnswer(ListingClassificationEntity row) =>
        new(
            row.Question,
            row.ResolvedChoice,
            row.IsApplicable,
            ClassificationReviewPolicy.NeedsReview(row, _reviewOptions.ReviewThreshold),
            row.TaxonomyVersionId);
}
