using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Data;

internal static class ListingSummaryMapper
{
    public static ListingSummary Build(ListingEntity l) =>
        new(
            l.ListingId,
            l.Title,
            l.Price,
            l.Currency,
            l.Url,
            l.IsSold,
            l.Condition,
            l.PrimaryImageUrl,
            l.BuyingFormat,
            l.Brand,
            l.OriginalPrice,
            l.Category,
            l.Likes,
            ListingImageUrlsJson.Deserialize(l.ImageUrls),
            l.SoldPrice,
            l.SoldDate,
            l.CategoryId,
            BuildCategoryHierarchy(l),
            l.BrandId,
            l.ConditionId,
            l.SizeName,
            l.ColorName,
            l.ShippingPayer,
            l.SellerId,
            ListingAttributesJson.Deserialize(l.Attributes));

    private static MercariCategoryHierarchy? BuildCategoryHierarchy(ListingEntity l) =>
        l.Category0Id is null && l.Category0Name is null
        && l.Category1Id is null && l.Category1Name is null
        && l.Category2Id is null && l.Category2Name is null
            ? null
            : new MercariCategoryHierarchy(
                l.Category0Id, l.Category0Name, l.Category1Id, l.Category1Name, l.Category2Id, l.Category2Name);
}
