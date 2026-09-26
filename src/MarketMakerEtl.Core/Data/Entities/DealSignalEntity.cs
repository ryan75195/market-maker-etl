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

    public decimal? ForwardNetMedian { get; set; }

    public int? ForwardSoldCount { get; set; }

    public decimal? RealisedMargin { get; set; }

    public double? ListingSoldWithinHours { get; set; }

    public bool UsedEstimatedDates { get; set; }

    public int? EvaluationWindowDays { get; set; }
}
