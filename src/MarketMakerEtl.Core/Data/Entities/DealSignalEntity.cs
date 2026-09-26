namespace MarketMakerEtl.Core.Data.Entities;

public sealed class DealSignalEntity
{
    public int Id { get; set; }

    public int ListingEntityId { get; set; }

    public int ProductFamilyId { get; set; }

    public int TaxonomyVersionId { get; set; }

    public string GroupKeyJson { get; set; } = string.Empty;

    public decimal LandedPrice { get; set; }

    public decimal? SoldNetMedian { get; set; }

    public int SoldCount { get; set; }

    public decimal? SoldP25 { get; set; }

    public decimal Discount { get; set; }

    public DateTime CreatedUtc { get; set; }
}
