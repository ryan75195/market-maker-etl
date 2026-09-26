namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ProductFamilyEntity
{
    public const decimal DefaultDealMinDiscount = 0.20m;

    public const int DefaultDealMinSold = 5;

    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string? DealGroupBy { get; set; }

    public decimal DealMinDiscount { get; set; } = DefaultDealMinDiscount;

    public int DealMinSold { get; set; } = DefaultDealMinSold;

    public DateTime CreatedUtc { get; set; }

    public ICollection<TaxonomyVersionEntity> TaxonomyVersions { get; } = new List<TaxonomyVersionEntity>();
}
