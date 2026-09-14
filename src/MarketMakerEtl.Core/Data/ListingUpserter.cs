using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Data;

internal static class ListingUpserter
{
    public static void Apply(
        EtlDbContext db,
        int jobId,
        Marketplace marketplace,
        ListingSummary listing,
        ListingEntity? existing)
    {
        if (existing is null)
        {
            db.Listings.Add(new ListingEntity
            {
                ListingId = listing.ListingId,
                ScrapeJobId = jobId,
                Marketplace = marketplace,
                Title = listing.Title,
                Price = listing.Price,
                Currency = listing.Currency,
                Url = listing.Url,
                IsSold = listing.IsSold,
                Condition = listing.Condition,
                PrimaryImageUrl = listing.PrimaryImageUrl,
                BuyingFormat = listing.BuyingFormat,
                CreatedUtc = DateTime.UtcNow
            });
            return;
        }

        existing.Title = listing.Title;
        existing.Price = listing.Price;
        existing.Currency = listing.Currency;
        existing.Url = listing.Url;
        existing.IsSold = listing.IsSold;
        existing.Condition = listing.Condition;
        existing.PrimaryImageUrl = listing.PrimaryImageUrl;
        existing.BuyingFormat = listing.BuyingFormat;
        existing.UpdatedUtc = DateTime.UtcNow;
    }
}
