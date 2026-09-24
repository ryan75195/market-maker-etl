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

        if (string.Equals(detail.Status, SoldStatus, StringComparison.Ordinal))
        {
            await ApplySoldDetail(db, listing, detail, wasSold, ct);
        }

        listing.DetailFetchedUtc = DateTime.UtcNow;
        listing.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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
        await db.SaveChangesAsync(ct);
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
