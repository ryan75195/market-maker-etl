namespace MarketMakerEtl.Core.Data.Entities;

public sealed class JobCategoryEntity
{
    public int Id { get; set; }

    public int ScrapeJobId { get; set; }

    public ScrapeJobEntity? ScrapeJob { get; set; }

    public int CategoryId { get; set; }

    public CategoryEntity? Category { get; set; }
}
