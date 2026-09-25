namespace MarketMakerEtl.Core.Data.Entities;

public sealed class TaxonomyVersionEntity
{
    public int Id { get; set; }

    public int ProductFamilyId { get; set; }

    public int Version { get; set; }

    public string QuestionsJson { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public ProductFamilyEntity? ProductFamily { get; set; }
}
