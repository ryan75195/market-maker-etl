using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Data;

internal static class ListingUpserter
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";

    public static void Apply(
        EtlDbContext db,
        int jobId,
        Marketplace marketplace,
        ListingSummary listing,
        ListingEntity? existing)
    {
        if (existing is null)
        {
            AddNew(db, jobId, marketplace, listing);
            return;
        }

        UpdateExisting(db, listing, existing);
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

    private static void UpdateExisting(EtlDbContext db, ListingSummary listing, ListingEntity existing)
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
            return;
        }

        if (!priceChanged)
        {
            return;
        }

        AddHistoryRow(
            db,
            existing,
            string.IsNullOrWhiteSpace(existing.ItemStatus) ? ActiveStatus : existing.ItemStatus,
            listing.Price,
            ListingHistorySource.PriceUpdate);
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
    }
}
