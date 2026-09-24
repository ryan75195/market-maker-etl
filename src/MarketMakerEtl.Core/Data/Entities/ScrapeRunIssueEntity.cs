namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ScrapeRunIssueEntity
{
    public int Id { get; set; }

    public int ScrapeRunId { get; set; }

    public string? ListingId { get; set; }

    public string IssueType { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string Phase { get; set; } = string.Empty;

    public int? HttpStatusCode { get; set; }

    public DateTime CreatedUtc { get; set; }
}
