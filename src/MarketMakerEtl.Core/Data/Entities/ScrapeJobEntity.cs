using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ScrapeJobEntity
{
    public int Id { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    public Marketplace Marketplace { get; set; } = Marketplace.Ebay;

    public string? FilterInstructions { get; set; }

    public int IntervalHours { get; set; } = 24;

    public bool IsEnabled { get; set; } = true;

    public DateTime? LastQueuedUtc { get; set; }

    public DateTime? LastRunUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<JobCategoryEntity> JobCategories { get; } = new List<JobCategoryEntity>();
}
