namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ScrapeRunEntity
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}
