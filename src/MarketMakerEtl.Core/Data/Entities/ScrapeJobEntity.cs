namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ScrapeJobEntity
{
    public int Id { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; }
}
