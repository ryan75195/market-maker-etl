using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ItemDetailStore : IItemDetailStore
{
    private const string SoldStatus = "Sold";
    private const string OkDescriptionStatus = "ok";
    private const string FailedDescriptionStatus = "failed";

    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ItemDetailStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetListingsNeedingDetail(
        int jobId, int limit, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.Listings
            .Where(l => l.ScrapeJobId == jobId && l.DetailFetchedUtc == null && l.DetailFetchAttempts < maxAttempts)
            .OrderByDescending(l => l.DetailFetchAttempts == 0 && l.IsSold)
            .ThenBy(l => l.DetailFetchAttempts)
            .ThenBy(l => l.Id)
            .Take(limit)
            .ToListAsync(ct);

        return listings
            .Select(l => new ListingDetailTarget(l.Id, l.ListingId, l.Url, l.ItemStatus, l.Marketplace))
            .ToList();
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetBacklogListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        if (jobIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        var idArray = jobIds.ToArray();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.Listings
            .Where(l => idArray.Contains(l.ScrapeJobId) && l.DetailFetchedUtc == null && l.DetailFetchAttempts < maxAttempts)
            .OrderBy(l => l.DetailFetchAttempts > 0 ? 2 : l.IsSold ? 0 : 1)
            .ThenByDescending(l => l.PostedUtc)
            .ThenByDescending(l => l.CreatedUtc)
            .ThenBy(l => l.DetailFetchAttempts)
            .ThenBy(l => l.Id)
            .Take(limit)
            .ToListAsync(ct);

        return listings
            .Select(l => new ListingDetailTarget(l.Id, l.ListingId, l.Url, l.ItemStatus, l.Marketplace))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetListingEntityIds(
        int jobId, IReadOnlyCollection<string> listingIds, CancellationToken ct)
    {
        if (listingIds.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var idArray = listingIds.ToArray();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var matches = await db.Listings
            .Where(l => l.ScrapeJobId == jobId && idArray.Contains(l.ListingId))
            .Select(l => new { l.ListingId, l.Id })
            .ToListAsync(ct);

        return matches.ToDictionary(m => m.ListingId, m => m.Id, StringComparer.Ordinal);
    }

    public async Task ApplyItemDetail(int listingEntityId, ItemPageListing detail, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingEntityId], ct);

        if (listing is null)
        {
            return;
        }

        var wasSold = listing.IsSold;
        ApplyDetailFields(listing, detail);
        await SellerUpserter.Apply(db, detail.SellerProfile, ct);
        await UpsertItemDetailRawData(db, listingEntityId, detail.RawJson, ct);

        if (string.Equals(detail.Status, SoldStatus, StringComparison.Ordinal))
        {
            await ApplySoldDetail(db, listing, detail, wasSold, ct);
        }

        listing.DetailFetchedUtc = DateTime.UtcNow;
        listing.UpdatedUtc = DateTime.UtcNow;
        await SqliteBusyRetry.ExecuteAsync(() => db.SaveChangesAsync(ct), ct);
    }

    private static async Task UpsertItemDetailRawData(
        EtlDbContext db, int listingEntityId, string? rawJson, CancellationToken ct)
    {
        if (rawJson is null)
        {
            return;
        }

        var gzip = GzipJson.Compress(rawJson);
        var existingRawData = await db.ListingRawData
            .FirstOrDefaultAsync(r => r.ListingEntityId == listingEntityId, ct);

        if (existingRawData is not null)
        {
            existingRawData.ItemDetailJsonGzip = gzip;
            existingRawData.ItemDetailJsonUpdatedUtc = DateTime.UtcNow;
            return;
        }

        db.ListingRawData.Add(new ListingRawDataEntity
        {
            ListingEntityId = listingEntityId,
            ItemDetailJsonGzip = gzip,
            ItemDetailJsonUpdatedUtc = DateTime.UtcNow
        });
    }

    public async Task MarkDetailFetchFailed(int listingEntityId, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingEntityId], ct);

        if (listing is null)
        {
            return;
        }

        listing.DetailFetchAttempts += 1;

        if (listing.DetailFetchAttempts >= maxAttempts)
        {
            listing.DescriptionStatus = FailedDescriptionStatus;
        }

        listing.UpdatedUtc = DateTime.UtcNow;
        await SqliteBusyRetry.ExecuteAsync(() => db.SaveChangesAsync(ct), ct);
    }

    private static void ApplyDetailFields(ListingEntity listing, ItemPageListing detail)
    {
        listing.Description = detail.Description ?? listing.Description;
        listing.DescriptionStatus = OkDescriptionStatus;
        listing.Seller = detail.Seller ?? listing.Seller;
        listing.ShippingCost = detail.ShippingCost ?? listing.ShippingCost;
        listing.OriginalPrice = detail.OriginalPrice ?? listing.OriginalPrice;
        listing.Likes = detail.Likes ?? listing.Likes;
        listing.PostedUtc = detail.PostedUtc?.UtcDateTime ?? listing.PostedUtc;

        if (detail.ImageUrls is { Count: > 0 })
        {
            listing.ImageUrls = ListingImageUrlsJson.Serialize(detail.ImageUrls);
        }

        ApplySegmentationDetailFields(listing, detail);
    }

    private static void ApplySegmentationDetailFields(ListingEntity listing, ItemPageListing detail)
    {
        listing.CategoryId = detail.CategoryId ?? listing.CategoryId;
        listing.Category0Id = detail.CategoryHierarchy?.Level0Id ?? listing.Category0Id;
        listing.Category0Name = detail.CategoryHierarchy?.Level0Name ?? listing.Category0Name;
        listing.Category1Id = detail.CategoryHierarchy?.Level1Id ?? listing.Category1Id;
        listing.Category1Name = detail.CategoryHierarchy?.Level1Name ?? listing.Category1Name;
        listing.Category2Id = detail.CategoryHierarchy?.Level2Id ?? listing.Category2Id;
        listing.Category2Name = detail.CategoryHierarchy?.Level2Name ?? listing.Category2Name;
        listing.BrandId = detail.BrandId ?? listing.BrandId;
        listing.ConditionId = detail.ConditionId ?? listing.ConditionId;
        listing.SizeName = detail.SizeName ?? listing.SizeName;
        listing.ColorName = detail.ColorName ?? listing.ColorName;
        listing.ShippingPayer = detail.ShippingPayer ?? listing.ShippingPayer;
        listing.ShipsFromState = detail.ShipsFromState ?? listing.ShipsFromState;
        listing.DiscountRatio = detail.DiscountRatio ?? listing.DiscountRatio;
        listing.SellerId = detail.SellerProfile?.SellerId ?? listing.SellerId;

        if (detail.Attributes is { Count: > 0 })
        {
            listing.Attributes = ListingAttributesJson.Serialize(detail.Attributes);
        }
    }

    private static async Task ApplySoldDetail(
        EtlDbContext db, ListingEntity listing, ItemPageListing detail, bool wasSold, CancellationToken ct)
    {
        var soldPrice = detail.SoldPrice ?? detail.Price ?? listing.SoldPrice;
        var soldDate = SoldDateParser.Parse(detail.SoldDate) ?? listing.SoldDate;

        listing.ItemStatus = SoldStatus;
        listing.IsSold = true;
        listing.SoldPrice = soldPrice;
        listing.SoldDate = soldDate;

        if (wasSold)
        {
            var existingSoldRow = await db.ListingStatusChanges
                .Where(h => h.ListingEntityId == listing.Id && h.Status == SoldStatus)
                .OrderByDescending(h => h.ChangedUtc)
                .FirstOrDefaultAsync(ct);

            if (existingSoldRow is not null)
            {
                existingSoldRow.SoldDateUtc = soldDate;
                existingSoldRow.Price ??= soldPrice;
                return;
            }
        }

        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            Listing = listing,
            Status = SoldStatus,
            Price = soldPrice,
            SoldDateUtc = soldDate,
            Source = ListingHistorySource.StatusUpdate,
            ChangedUtc = DateTime.UtcNow
        });
    }
}
