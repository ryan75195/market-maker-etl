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
        ApplySummary(entity, listing);
        db.Listings.Add(entity);

        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            Listing = entity,
            Status = listing.IsSold ? SoldStatus : ActiveStatus,
            Price = listing.Price,
            Source = ListingHistorySource.InitialScrape,
            ChangedUtc = DateTime.UtcNow
        });
    }

    private static void UpdateExisting(EtlDbContext db, ListingSummary listing, ListingEntity existing)
    {
        var priceChanged = existing.Price != listing.Price;
        ApplySummary(existing, listing);
        existing.UpdatedUtc = DateTime.UtcNow;

        if (!priceChanged)
        {
            return;
        }

        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            ListingEntityId = existing.Id,
            Status = string.IsNullOrWhiteSpace(existing.ItemStatus) ? ActiveStatus : existing.ItemStatus,
            Price = listing.Price,
            Source = ListingHistorySource.PriceUpdate,
            ChangedUtc = DateTime.UtcNow
        });
    }

    private static void ApplySummary(ListingEntity entity, ListingSummary listing)
    {
        entity.Title = listing.Title;
        entity.Price = listing.Price;
        entity.Currency = listing.Currency;
        entity.Url = listing.Url;
        entity.IsSold = listing.IsSold;
        entity.Condition = listing.Condition;
        entity.PrimaryImageUrl = listing.PrimaryImageUrl;
        entity.BuyingFormat = listing.BuyingFormat;
        entity.Brand = listing.Brand;
        entity.OriginalPrice = listing.OriginalPrice;
        entity.Category = listing.Category;
        entity.Likes = listing.Likes;
        entity.ImageUrls = ListingImageUrlsJson.Serialize(listing.ImageUrls);
    }
}
