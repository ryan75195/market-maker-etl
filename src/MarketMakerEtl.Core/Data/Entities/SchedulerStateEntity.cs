namespace MarketMakerEtl.Core.Data.Entities;

public sealed class SchedulerStateEntity
{
    public int Id { get; set; }

    public DateTime? LastListingRefreshUtc { get; set; }
}
