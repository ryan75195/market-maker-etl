namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ListingEntity
{
    public int Id { get; set; }

    public string ListingId { get; set; } = string.Empty;

    public int ScrapeJobId { get; set; }

    public string? Title { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Url { get; set; }

    public bool IsSold { get; set; }

    public string? Condition { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public string? BuyingFormat { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? UpdatedUtc { get; set; }
}
