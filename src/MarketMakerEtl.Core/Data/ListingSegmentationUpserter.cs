using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Data;

internal static class ListingSegmentationUpserter
{
    public static void ApplyNew(ListingEntity entity, ListingSummary listing)
    {
        entity.CategoryId = listing.CategoryId;
        entity.Category0Id = listing.CategoryHierarchy?.Level0Id;
        entity.Category0Name = listing.CategoryHierarchy?.Level0Name;
        entity.Category1Id = listing.CategoryHierarchy?.Level1Id;
        entity.Category1Name = listing.CategoryHierarchy?.Level1Name;
        entity.Category2Id = listing.CategoryHierarchy?.Level2Id;
        entity.Category2Name = listing.CategoryHierarchy?.Level2Name;
        entity.BrandId = listing.BrandId;
        entity.ConditionId = listing.ConditionId;
        entity.SizeName = listing.SizeName;
        entity.ColorName = listing.ColorName;
        entity.ShippingPayer = listing.ShippingPayer;
        entity.SellerId = listing.SellerId;
        entity.Attributes = ListingAttributesJson.Serialize(listing.Attributes);
    }

    public static void ApplyEnrichment(ListingEntity entity, ListingSummary listing)
    {
        entity.CategoryId = listing.CategoryId ?? entity.CategoryId;
        entity.Category0Id = listing.CategoryHierarchy?.Level0Id ?? entity.Category0Id;
        entity.Category0Name = listing.CategoryHierarchy?.Level0Name ?? entity.Category0Name;
        entity.Category1Id = listing.CategoryHierarchy?.Level1Id ?? entity.Category1Id;
        entity.Category1Name = listing.CategoryHierarchy?.Level1Name ?? entity.Category1Name;
        entity.Category2Id = listing.CategoryHierarchy?.Level2Id ?? entity.Category2Id;
        entity.Category2Name = listing.CategoryHierarchy?.Level2Name ?? entity.Category2Name;
        entity.BrandId = listing.BrandId ?? entity.BrandId;
        entity.ConditionId = listing.ConditionId ?? entity.ConditionId;
        entity.SizeName = listing.SizeName ?? entity.SizeName;
        entity.ColorName = listing.ColorName ?? entity.ColorName;
        entity.ShippingPayer = listing.ShippingPayer ?? entity.ShippingPayer;
        entity.SellerId = listing.SellerId ?? entity.SellerId;

        if (listing.Attributes is { Count: > 0 })
        {
            entity.Attributes = ListingAttributesJson.Serialize(listing.Attributes);
        }
    }

    public static void UpsertRawData(
        EtlDbContext db, ListingEntity existing, ListingRawDataEntity? existingRawData, string? rawJson)
    {
        if (rawJson is null)
        {
            return;
        }

        var gzip = GzipJson.Compress(rawJson);

        if (existingRawData is not null)
        {
            existingRawData.SearchItemJsonGzip = gzip;
            existingRawData.SearchItemJsonUpdatedUtc = DateTime.UtcNow;
            return;
        }

        db.ListingRawData.Add(new ListingRawDataEntity
        {
            ListingEntityId = existing.Id,
            SearchItemJsonGzip = gzip,
            SearchItemJsonUpdatedUtc = DateTime.UtcNow
        });
    }
}
