using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ListingEntity
{
    public int Id { get; set; }

    public string ListingId { get; set; } = string.Empty;

    public int ScrapeJobId { get; set; }

    public Marketplace Marketplace { get; set; } = Marketplace.Ebay;

    public string? Title { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Url { get; set; }

    public bool IsSold { get; set; }

    public string? Condition { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public string? BuyingFormat { get; set; }

    public string? Brand { get; set; }

    public string? ItemStatus { get; set; }

    public decimal? SoldPrice { get; set; }

    public DateTime? SoldDate { get; set; }

    public string? Seller { get; set; }

    public decimal? ShippingCost { get; set; }

    public string? Description { get; set; }

    public string? DescriptionStatus { get; set; }

    public string? ImageUrls { get; set; }

    public decimal? OriginalPrice { get; set; }

    public string? Category { get; set; }

    public int? Likes { get; set; }

    public DateTime? PostedUtc { get; set; }

    public DateTime? DetailFetchedUtc { get; set; }

    public int DetailFetchAttempts { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? UpdatedUtc { get; set; }

    public int? CategoryId { get; set; }

    public int? Category0Id { get; set; }

    public string? Category0Name { get; set; }

    public int? Category1Id { get; set; }

    public string? Category1Name { get; set; }

    public int? Category2Id { get; set; }

    public string? Category2Name { get; set; }

    public int? BrandId { get; set; }

    public int? ConditionId { get; set; }

    public string? SizeName { get; set; }

    public string? ColorName { get; set; }

    public string? ShippingPayer { get; set; }

    public string? ShipsFromState { get; set; }

    public long? SellerId { get; set; }

    public int? DiscountRatio { get; set; }

    public string? Attributes { get; set; }

    public ListingRawDataEntity? RawData { get; set; }
}
