namespace MarketMakerEtl.Core.Data.Entities;

public sealed class FetchOutcomeBucketEntity
{
    public int Id { get; set; }

    public DateTime BucketStartUtc { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int Count { get; set; }
}
