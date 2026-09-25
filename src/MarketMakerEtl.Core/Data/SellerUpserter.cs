using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Data;

internal static class SellerUpserter
{
    public static async Task Apply(EtlDbContext db, MercariSellerProfile? profile, CancellationToken ct)
    {
        if (profile is null)
        {
            return;
        }

        var seller = await db.Sellers.FindAsync([profile.SellerId], ct);

        if (seller is null)
        {
            seller = new SellerEntity { SellerId = profile.SellerId };
            db.Sellers.Add(seller);
        }

        ApplyFields(seller, profile);
    }

    private static void ApplyFields(SellerEntity seller, MercariSellerProfile profile)
    {
        seller.Name = profile.Name ?? seller.Name;
        seller.NumSales = profile.NumSales ?? seller.NumSales;
        seller.NumSellItems = profile.NumSellItems ?? seller.NumSellItems;
        seller.RatingCount = profile.RatingCount ?? seller.RatingCount;
        seller.RatingAverage = profile.RatingAverage ?? seller.RatingAverage;
        seller.IsProSeller = profile.IsProSeller ?? seller.IsProSeller;
        seller.AccountCreatedUtc = profile.AccountCreatedUtc?.UtcDateTime ?? seller.AccountCreatedUtc;
        seller.LastSeenUtc = DateTime.UtcNow;
    }
}
