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
        int jobId, int limit, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.Listings
            .Where(l => l.ScrapeJobId == jobId && l.DetailFetchedUtc == null)
            .OrderBy(l => l.Id)
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

        ApplyDetailFields(listing, detail);

        if (string.Equals(detail.Status, SoldStatus, StringComparison.Ordinal))
        {
            ApplySoldDetail(db, listing, detail);
        }

        listing.DetailFetchedUtc = DateTime.UtcNow;
        listing.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDetailFetchFailed(int listingEntityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingEntityId], ct);

        if (listing is null)
        {
            return;
        }

        listing.DescriptionStatus = FailedDescriptionStatus;
        listing.DetailFetchedUtc = DateTime.UtcNow;
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

    private static void ApplySoldDetail(EtlDbContext db, ListingEntity listing, ItemPageListing detail)
    {
        var soldPrice = detail.SoldPrice ?? detail.Price ?? listing.SoldPrice;
        var soldDate = SoldDateParser.Parse(detail.SoldDate) ?? listing.SoldDate;

        listing.ItemStatus = SoldStatus;
        listing.IsSold = true;
        listing.SoldPrice = soldPrice;
        listing.SoldDate = soldDate;

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
