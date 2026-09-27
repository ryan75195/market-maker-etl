namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ClassificationBatchRunEntity
{
    public int Id { get; set; }

    public DateTime RanUtc { get; set; }

    public bool Succeeded { get; set; }
}
