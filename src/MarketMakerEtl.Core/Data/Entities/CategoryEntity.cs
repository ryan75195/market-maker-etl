namespace MarketMakerEtl.Core.Data.Entities;

public sealed class CategoryEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; }
}
