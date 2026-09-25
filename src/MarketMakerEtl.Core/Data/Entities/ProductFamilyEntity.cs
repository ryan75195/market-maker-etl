namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ProductFamilyEntity
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public ICollection<TaxonomyVersionEntity> TaxonomyVersions { get; } = new List<TaxonomyVersionEntity>();
}
