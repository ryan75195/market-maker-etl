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

    public DateTime CreatedUtc { get; set; }

    public DateTime? UpdatedUtc { get; set; }
}
