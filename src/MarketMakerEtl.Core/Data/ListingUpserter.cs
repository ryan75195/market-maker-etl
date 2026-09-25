using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

internal enum ListingUpsertOutcome
{
    AddedActive,
    AddedSold,
    Updated,
    Skipped
}

internal static class ListingUpserter
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";

    public static ListingUpsertOutcome Apply(
        EtlDbContext db,
        int jobId,
        Marketplace marketplace,
        ListingSummary listing,
        ListingEntity? existing,
        ListingRawDataEntity? existingRawData)
    {
        if (existing is null)
        {
            AddNew(db, jobId, marketplace, listing);
            return listing.IsSold ? ListingUpsertOutcome.AddedSold : ListingUpsertOutcome.AddedActive;
        }

        ListingSegmentationUpserter.UpsertRawData(db, existing, existingRawData, listing.RawJson);
        return UpdateExisting(db, listing, existing);
    }

    public static async Task<ListingUpsertSummary> ApplyAll(
        EtlDbContext db,
        int jobId,
        Marketplace marketplace,
        IReadOnlyList<ListingSummary> listings,
        CancellationToken ct)
    {
        var addedActive = 0;
        var addedSold = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var listing in listings.GroupBy(l => l.ListingId).Select(g => g.Last()))
        {
            var existing = await db.Listings.FirstOrDefaultAsync(l => l.ListingId == listing.ListingId, ct);
            var existingRawData = existing is null
                ? null
                : await db.ListingRawData.FirstOrDefaultAsync(r => r.ListingEntityId == existing.Id, ct);
            switch (Apply(db, jobId, marketplace, listing, existing, existingRawData))
            {
                case ListingUpsertOutcome.AddedActive:
                    addedActive++;
                    break;
                case ListingUpsertOutcome.AddedSold:
                    addedSold++;
                    break;
                case ListingUpsertOutcome.Updated:
                    updated++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        return new ListingUpsertSummary(addedActive, addedSold, updated, skipped);
    }

    private static void AddNew(EtlDbContext db, int jobId, Marketplace marketplace, ListingSummary listing)
    {
        var entity = new ListingEntity
        {
            ListingId = listing.ListingId,
            ScrapeJobId = jobId,
            Marketplace = marketplace,
            CreatedUtc = DateTime.UtcNow
        };
        ApplyNewListingFields(entity, listing);

        if (listing.RawJson is not null)
        {
            entity.RawData = new ListingRawDataEntity
            {
                SearchItemJsonGzip = GzipJson.Compress(listing.RawJson),
                SearchItemJsonUpdatedUtc = DateTime.UtcNow
            };
        }

        db.Listings.Add(entity);

        if (listing.IsSold)
        {
            entity.SoldPrice ??= listing.Price;
        }

        AddHistoryRow(
            db,
            entity,
            listing.IsSold ? SoldStatus : ActiveStatus,
            listing.Price,
            ListingHistorySource.InitialScrape);
    }

    private static ListingUpsertOutcome UpdateExisting(EtlDbContext db, ListingSummary listing, ListingEntity existing)
    {
        var priceChanged = listing.Price is not null && existing.Price != listing.Price;
        var becameSold = !existing.IsSold && listing.IsSold;

        ApplyCoreSearchFields(existing, listing);
        ApplyEnrichmentFields(existing, listing);
        existing.UpdatedUtc = DateTime.UtcNow;

        if (becameSold)
        {
            existing.ItemStatus = SoldStatus;
            existing.SoldPrice ??= listing.Price;
            AddHistoryRow(db, existing, SoldStatus, existing.SoldPrice, ListingHistorySource.StatusUpdate);
            return ListingUpsertOutcome.Updated;
        }

        if (!priceChanged)
        {
            return ListingUpsertOutcome.Skipped;
        }

        AddHistoryRow(
            db,
            existing,
            string.IsNullOrWhiteSpace(existing.ItemStatus) ? ActiveStatus : existing.ItemStatus,
            listing.Price,
            ListingHistorySource.PriceUpdate);
        return ListingUpsertOutcome.Updated;
    }

    private static void AddHistoryRow(EtlDbContext db, ListingEntity entity, string status, decimal? price, string source)
    {
        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            Listing = entity,
            Status = status,
            Price = price,
            Source = source,
            ChangedUtc = DateTime.UtcNow
        });
    }

    private static void ApplyNewListingFields(ListingEntity entity, ListingSummary listing)
    {
        entity.Title = listing.Title;
        entity.Price = listing.Price;
        entity.Currency = listing.Currency;
        entity.Url = listing.Url;
        entity.IsSold = listing.IsSold;
        entity.ItemStatus = listing.IsSold ? SoldStatus : ActiveStatus;
        entity.Condition = listing.Condition;
        entity.PrimaryImageUrl = listing.PrimaryImageUrl;
        entity.BuyingFormat = listing.BuyingFormat;
        entity.Brand = listing.Brand;
        entity.OriginalPrice = listing.OriginalPrice;
        entity.Category = listing.Category;
        entity.Likes = listing.Likes;
        entity.ImageUrls = ListingImageUrlsJson.Serialize(listing.ImageUrls);
        ListingSegmentationUpserter.ApplyNew(entity, listing);
    }

    private static void ApplyCoreSearchFields(ListingEntity entity, ListingSummary listing)
    {
        entity.Title = listing.Title;
        entity.Currency = listing.Currency;
        entity.Url = listing.Url;
        entity.IsSold = listing.IsSold;
        entity.BuyingFormat = listing.BuyingFormat;

        if (listing.Price is not null)
        {
            entity.Price = listing.Price;
        }
    }

    private static void ApplyEnrichmentFields(ListingEntity entity, ListingSummary listing)
    {
        entity.Condition = listing.Condition ?? entity.Condition;
        entity.PrimaryImageUrl = listing.PrimaryImageUrl ?? entity.PrimaryImageUrl;
        entity.Brand = listing.Brand ?? entity.Brand;
        entity.OriginalPrice = listing.OriginalPrice ?? entity.OriginalPrice;
        entity.Category = listing.Category ?? entity.Category;
        entity.Likes = listing.Likes ?? entity.Likes;

        if (listing.ImageUrls is { Count: > 0 })
        {
            entity.ImageUrls = ListingImageUrlsJson.Serialize(listing.ImageUrls);
        }

        ListingSegmentationUpserter.ApplyEnrichment(entity, listing);
    }
}
