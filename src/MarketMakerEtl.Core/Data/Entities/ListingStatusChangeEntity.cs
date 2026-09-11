namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ListingStatusChangeEntity
{
    public int Id { get; set; }

    public int ListingEntityId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime ChangedUtc { get; set; }

    public ListingEntity? Listing { get; set; }
}
