using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ScrapeRunEntity
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public Marketplace Marketplace { get; set; } = Marketplace.Ebay;

    public string SearchTerm { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string TriggerType { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public int ListingsAddedActive { get; set; }

    public int ListingsAddedSold { get; set; }

    public int ListingsUpdated { get; set; }

    public int ListingsSkipped { get; set; }

    public int ListingsFailed { get; set; }

    public int TotalListingsFound { get; set; }

    public int? TotalReportedBySearch { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? SearchCompletedUtc { get; set; }

    public DateTime? DetailCompletedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}
