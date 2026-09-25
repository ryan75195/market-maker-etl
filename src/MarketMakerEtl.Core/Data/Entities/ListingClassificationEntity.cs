using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ListingClassificationEntity
{
    public int Id { get; set; }

    public int ListingEntityId { get; set; }

    public int TaxonomyVersionId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Choice { get; set; } = string.Empty;

    public string? ResolvedChoice { get; set; }

    public bool IsApplicable { get; set; }

    public double Confidence { get; set; }

    public double Agreement { get; set; }

    public string ProbabilitiesJson { get; set; } = string.Empty;

    public ClassificationSource Source { get; set; } = ClassificationSource.Model;

    public DateTime ClassifiedUtc { get; set; }
}
